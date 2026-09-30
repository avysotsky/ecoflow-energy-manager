# EcoFlow.EnergyManager

Локальная система прогноза солнечной генерации и безопасного управления **Backup Reserve**
для EcoFlow DELTA 2 Max. Основная бизнес-логика реализована на .NET 8; доступ к станции
выполняется локально по Bluetooth LE через отдельный Python-адаптер. EcoFlow Cloud не
используется для штатного чтения статуса и управления.

## Возможности

- получает почасовой прогноз на завтра из keyless Open-Meteo по моделям ECMWF, ICON,
  GFS и AIFS;
- рассчитывает ожидаемую генерацию массива солнечных панелей с поправкой на температуру;
- сохраняет исходные прогнозы, расчёт генерации, фактическую температуру, метрики
  качества моделей и фактическую мощность XT60 в PostgreSQL;
- выбирает модель по MAE/RMSE за скользящее окно, а при недостаточной истории использует
  равновесный ансамбль;
- рассчитывает и, если управление разрешено, применяет Backup Reserve по линейной политике;
- проверяет BLE-аутентификацию, свежесть данных, границы, persistent rate limit,
  manual override, идемпотентность и независимый readback;
- пишет решения и результаты управления в локальный JSONL-аудит;
- показывает обезличенный статус в локальном dashboard на русском, украинском и английском;
- каждую минуту сохраняет фактическую входную мощность двух XT60;
- отправляет итог каждого штатного и ручного прогноза в Telegram, включая частично
  доступные данные при ошибке.

## Архитектура

```text
Open-Meteo (keyless HTTPS: GTI + температура)
             |
             v
EcoFlow.EnergyManager (.NET 8 / ASP.NET Core / BackgroundService)
  |          |--> PostgreSQL: прогнозы, MAE/RMSE, XT60 actuals
  |          |--> SolarCalculator -> policy -> guards -> audit
  |          '--> локальный Telegram sender
  |
  '-- HTTP http://127.0.0.1:8765
             |
             v
ecoflow-local (Python 3 / ha-ef-ble / BlueZ)
             |
             v
Bluetooth LE -> EcoFlow DELTA 2 Max
```

Dashboard и его API по умолчанию слушают только `http://127.0.0.1:5095`. BLE bridge
слушает только `http://127.0.0.1:8765`.

> **Важно:** исходники bridge не входят в этот Git-репозиторий. Это отдельный локальный
> инфраструктурный адаптер `projects/ecoflow-local`, развёрнутый на Linux-хосте рядом
> с приложением. Репозиторий содержит только .NET-клиент его подтверждённого API и тесты
> контракта.

## Режимы запуска

### Штатный режим

Сервис постоянно запускает три фоновые задачи:

- прогноз и расчёт на следующий день — ежедневно в **23:00 Europe/Kyiv**;
- сбор фактической температуры Open-Meteo Best Match — при старте и на 5-й минуте
  каждого часа;
- сбор фактической мощности XT60 — раз в минуту.

Час прогноза и IANA timezone настраиваются. Переходы на летнее/зимнее время рассчитываются
через системную базу временных зон.

### Ручной one-shot

```bash
dotnet run --project src/EcoFlow.EnergyManager -- --once
```

`--once` выполняет тот же прогноз, выбор модели, расчёт, guarded control, аудит и
Telegram outcome, после чего завершает процесс. Ручной запуск **не отключает** persistent
rate limit автоматически. Для санкционированного внепланового запуска допустимо временно
передать безопасные environment overrides процессу, не меняя постоянную конфигурацию.

Отдельный однократный сбор фактической температуры:

```bash
dotnet run --project src/EcoFlow.EnergyManager -- --collect-actual
```

## Прогноз инсоляции и выбор модели

Приложение вызывает keyless Open-Meteo Forecast API с координатами, углом наклона и
азимутом массива и получает на завтра:

- `global_tilted_irradiance` (GTI), Вт/м²;
- `temperature_2m`, °C;
- отдельные ряды моделей `ecmwf_ifs025`, `icon_seamless`, `gfs_seamless`,
  `ecmwf_aifs025_single`.

Для каждой модели сохраняются почасовые значения и итоговая генерация. Фактическая
температура Open-Meteo Best Match сопоставляется с ранее сохранёнными прогнозами.
За последние 60 дней вычисляются MAE и RMSE. Модель допускается к автоматическому выбору
после 24 сопоставленных наблюдений; выигрывает минимальная MAE, затем минимальная RMSE.
До накопления достаточной истории используется равновесное среднее всех полных моделей.
Полным считается прогноз минимум с 20 почасовыми значениями.

### Расчёт генерации

Для каждого часа:

```text
T_panel = T_air + GTI * 0.03
K_temp  = clamp(1 + K_coefficient * (T_panel - 25), 0.5, 1.1)
E_hour  = P_nominal * (GTI / 1000) * K_temp * system_efficiency
```

Где:

- `T_air` — температура воздуха, °C;
- `T_panel` — оценка температуры панели, °C;
- `GTI` — инсоляция на наклонную поверхность, Вт/м²;
- `K_coefficient` — температурный коэффициент, по умолчанию `-0.004/°C`;
- `P_nominal` — номинальная мощность, по умолчанию `1.0 kW`;
- `system_efficiency` — суммарная эффективность системы, по умолчанию `0.85`;
- `E_hour` — энергия за час, кВт·ч.

Суточный прогноз — сумма `E_hour`. Базовая конфигурация: Одесса
(`46.4775, 30.7326`), панели 1 kW, наклон 45°, южный азимут Open-Meteo `0°`.

## Политика Backup Reserve

Пусть `E` — ожидаемая генерация на завтра в кВт·ч:

```text
E <= 1.0  -> 100%
E >= 6.0  -> 20%
иначе     -> round(116 - 16 * E), MidpointRounding.AwayFromZero
```

Результат дополнительно ограничивается безопасным диапазоном `20..100%`.

| E, кВт·ч | Backup Reserve |
|---:|---:|
| ≤ 1.0 | 100% |
| 1.5 | 92% |
| 2.0 | 84% |
| 2.5 | 76% |
| 3.0 | 68% |
| 3.5 | 60% |
| 4.0 | 52% |
| 4.5 | 44% |
| 5.0 | 36% |
| 5.5 | 28% |
| ≥ 6.0 | 20% |

**Backup Reserve не является верхним лимитом заряда.** Reserve определяет долю батареи,
зарезервированную для резервного питания/energy management, тогда как charge upper
ограничивает максимальный SOC. Автоматизация изменяет только Backup Reserve. Charge
upper, нижний лимит заряда и выходы AC/DC доступны приложению только как статус и этой
политикой не меняются.

## Защита управления

Исходная конфигурация поставляется с `ECOFLOW_CONTROL_ENABLED=false`. Перед командой
проверяются:

1. BLE connected и authenticated;
2. свежесть статуса и прогноза;
3. наличие и включённое состояние Backup Reserve;
4. целое целевое значение в разрешённых границах;
5. отсутствие файла manual override;
6. persistent minimum interval после последней реально применённой записи;
7. идемпотентность — совпадающее значение не записывается повторно.

Команды сериализуются. Bridge выполняет собственные проверки model/auth/freshness/bounds,
использует подтверждённый метод `ha-ef-ble`
`set_energy_backup_battery_level` и ждёт readback
`energy_backup_battery_level`. Затем .NET выполняет отдельное повторное чтение статуса.
Успех записи фиксируется только после обоих подтверждений. Состояние rate limit и JSONL
аудит находятся вне репозитория.

Чтобы временно запретить автоматические записи без остановки мониторинга:

```bash
touch ~/.local/share/ecoflow-energy-manager/manual-override
```

Для возобновления:

```bash
rm ~/.local/share/ecoflow-energy-manager/manual-override
```

## Фактическая солнечная генерация

Bridge возвращает мощность XT60 input 1, XT60 input 2 и их сумму. Каждую минуту
`PvActualWorker` принимает только authenticated, свежие и физически допустимые значения
(`0..2500 W`) и сохраняет sample в `solar_actual_samples` с источником
`ecoflow_ble_xt60`. Это измерение входной мощности, а не уже интегрированная суточная
энергия.

## PostgreSQL

`ECOFLOW_POSTGRES_CONNECTION` обязателен. Схема создаётся приложением автоматически:

- `weather_forecast_runs`;
- `weather_forecast_hourly`;
- `solar_generation_forecasts`;
- `weather_actual_hourly`;
- `weather_model_accuracy_snapshots`;
- `solar_actual_samples`.

Рекомендуемое локальное подключение использует Unix socket и peer authentication.
Пароль БД в репозитории не нужен.

## Dashboard и API

Локальный dashboard показывает подключение/аутентификацию, SOC, входную/выходную/сетевую
мощность, XT60, AC и 12 V, read-only charge limits, Backup Reserve, время sample и
stale/error status.

Локальные endpoints:

- `GET /api/status` — обезличенный статус;
- `GET /api/settings` — безопасные runtime settings;
- `PUT /api/settings` — проверка, атомарное сохранение и применение settings без restart.

API не возвращает User ID, serial, bridge URL, строку подключения к БД или raw BLE errors.
Веб-интерфейс не предоставляет write endpoint управления устройством. Изменяемые settings:
координаты, timezone, параметры панелей/расчёта, час запуска и freshness limits.

## Telegram outcome

После **каждого** штатного запуска прогноза и `--once` вызывается настроенный локальный
sender. Сообщение содержит:

- дату прогноза;
- выбранную модель;
- ожидаемую генерацию;
- Backup Reserve до, рассчитанную цель и подтверждённый readback;
- общий успех или ошибку.

При forecast/control failure отправляются доступные к моменту ошибки поля. Вызов sender
ограничен timeout; штатный sender выполняет bounded retries. Ошибка доставки
диагностируется, но не отменяет и не маскирует уже безопасно выполненное управление.
Команда sender получает сообщение отдельным аргументом без shell interpolation.

Токен, chat ID и другие данные Telegram хранятся только в защищённой локальной
конфигурации sender вне репозитория. Не добавляйте их в environment example, unit,
аргументы запуска или логи.

## Конфигурация

Пример без секретов: `deploy/energy-manager.env.example`.

| Переменная | Назначение |
|---|---|
| `ECOFLOW_LATITUDE`, `ECOFLOW_LONGITUDE` | координаты массива |
| `ECOFLOW_TIMEZONE` | IANA timezone |
| `ECOFLOW_NOMINAL_POWER_KW` | номинальная мощность панелей |
| `ECOFLOW_PANEL_TILT`, `ECOFLOW_PANEL_AZIMUTH` | геометрия массива |
| `ECOFLOW_SYSTEM_EFFICIENCY` | системная эффективность |
| `ECOFLOW_TEMPERATURE_COEFFICIENT` | температурный коэффициент |
| `ECOFLOW_FORECAST_RUN_HOUR` | локальный час штатного расчёта |
| `ECOFLOW_WEATHER_MODELS` | список моделей Open-Meteo |
| `ECOFLOW_ACCURACY_WINDOW_DAYS` | окно оценки моделей |
| `ECOFLOW_MIN_ACCURACY_SAMPLES` | минимум observations для выбора модели |
| `ECOFLOW_POSTGRES_CONNECTION` | строка подключения PostgreSQL |
| `ECOFLOW_MAX_STATUS_AGE_MINUTES` | допустимый возраст BLE status |
| `ECOFLOW_MAX_FORECAST_AGE_MINUTES` | допустимый возраст forecast |
| `ECOFLOW_CONTROL_ENABLED` | разрешение реального reserve control |
| `ECOFLOW_CONTROL_MIN_BACKUP_RESERVE`, `ECOFLOW_CONTROL_MAX_BACKUP_RESERVE` | safety bounds |
| `ECOFLOW_CONTROL_MIN_INTERVAL_MINUTES` | persistent rate limit |
| `ECOFLOW_MANUAL_OVERRIDE_FILE` | файл блокировки auto-control |
| `ECOFLOW_CONTROL_STATE_PATH` | состояние последней записи |
| `ECOFLOW_DECISION_LOG` | JSONL audit |
| `ECOFLOW_BRIDGE_URL` | URL локального BLE bridge |
| `OPEN_METEO_URL` | endpoint Open-Meteo |
| `ECOFLOW_WEB_URLS` | адрес ASP.NET Core listener |
| `ECOFLOW_SETTINGS_PATH` | runtime settings JSON |
| `ECOFLOW_TELEGRAM_COMMAND` | абсолютный путь к локальному sender |
| `ECOFLOW_TELEGRAM_TIMEOUT_SECONDS` | общий timeout sender |

Секреты и локальные идентификаторы не должны находиться в Git:

- EcoFlow User ID и BLE authentication state;
- serial устройства;
- пароли/connection strings с паролями;
- Telegram token/chat ID;
- локальные runtime state, audit logs и settings.

## Сборка, запуск и тесты

Требования:

- Linux с BlueZ и Bluetooth-контроллером — для bridge;
- .NET SDK 8;
- Python 3 и совместимая версия `ha-ef-ble` — для внешнего bridge;
- PostgreSQL;
- доступ к keyless Open-Meteo API.

```bash
dotnet restore EcoFlow.EnergyManager.sln
dotnet build EcoFlow.EnergyManager.sln -c Release
dotnet test EcoFlow.EnergyManager.sln -c Release
```

Для разработки можно открыть `EcoFlow.EnergyManager.sln` в Visual Studio 2022+.

## Развёртывание через systemd

Репозиторий содержит:

- `deploy/ecoflow-energy-manager.service` — user unit;
- `deploy/ecoflow-energy-manager-control.conf` — reviewed drop-in активного control;
- `deploy/energy-manager.env.example` — безопасный шаблон environment.

Типовая последовательность на целевом Linux-хосте:

```bash
dotnet build EcoFlow.EnergyManager.sln -c Release
install -d ~/.config/systemd/user/ecoflow-energy-manager.service.d ~/.config/ecoflow
install -m 0644 deploy/ecoflow-energy-manager.service ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now ecoflow-ble-bridge.service
systemctl --user enable --now ecoflow-energy-manager.service
```

Environment-файл создаётся вручную вне репозитория с mode `0600`. Перед включением
control проверьте bridge, модель устройства, bounds, manual override и readback. Только
после такой проверки активный drop-in можно установить отдельно:

```bash
install -m 0644 deploy/ecoflow-energy-manager-control.conf ~/.config/systemd/user/ecoflow-energy-manager.service.d/control.conf
systemctl --user daemon-reload
systemctl --user restart ecoflow-energy-manager.service
```

Проверка:

```bash
systemctl --user status ecoflow-ble-bridge.service ecoflow-energy-manager.service --no-pager
curl -fsS http://127.0.0.1:8765/health
curl -fsS http://127.0.0.1:5095/api/status
journalctl --user -u ecoflow-energy-manager.service --no-pager -n 100
```

## Troubleshooting

- **BLE не подключается:** закройте/отключите EcoFlow phone app — устройство допускает
  только одно активное BLE-подключение; затем проверьте BlueZ и journal bridge.
- **Control blocked / stale:** проверьте время `sampled_utc`, connectivity/authentication
  bridge и freshness limits.
- **Control rate limit is active:** дождитесь интервала. Не удаляйте state для обхода
  штатного ограничения; для санкционированного one-shot используйте только временный
  process-level override.
- **Manual override is active:** убедитесь, что блокировка действительно должна быть снята,
  затем удалите только документированный marker file.
- **Open-Meteo error/неполный день:** проверьте сеть, endpoint, список models и наличие
  минимум 20 samples.
- **PostgreSQL error:** проверьте доступность БД, peer/user permissions и environment.
- **Telegram failed:** проверьте локальный sender и его защищённый environment; reserve
  control не откатывается из-за ошибки Telegram.
- **Readback mismatch:** не повторяйте команды вслепую; проверьте journal bridge, свежий
  статус и отсутствие второго BLE-клиента.

## Technology stack

- **.NET 8 / C#**;
- **ASP.NET Core**, Minimal API, static HTML/CSS/JavaScript dashboard;
- **BackgroundService**, **HttpClient**, **System.Text.Json**;
- **Npgsql / PostgreSQL**;
- **xUnit**;
- **Python 3**, **ha-ef-ble**, **BlueZ**, **Bluetooth LE** — отдельный локальный bridge;
- **Open-Meteo** keyless forecast/current APIs;
- **systemd user services**;
- локальный **Telegram sender** OpenClaw;
- **Linux**.

## Ограничения и безопасность

- Решение рассчитано на подтверждённую модель DELTA 2 Max и локальный single-device bridge.
- Dashboard/bridge должны оставаться localhost-only.
- Не публикуйте User ID, serial, токены, auth/session material или локальные state files.
- Не подменяйте Backup Reserve нижним/верхним charge limit.
- Автоматизация не управляет AC/DC outputs.
- Измерения XT60 пока сохраняются как power samples; интеграция в фактическую суточную
  энергию и автоматическая калибровка потерь не заявлены.
