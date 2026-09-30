# EcoFlow.EnergyManager

A local system for forecasting solar generation and safely managing **Backup Reserve**
for the EcoFlow DELTA 2 Max. The core business logic is implemented in .NET 8; the
power station is accessed locally over Bluetooth LE through a separate Python adapter.
EcoFlow Cloud is not used for normal status reads or control.

## Features

- fetches an hourly forecast for tomorrow from the keyless Open-Meteo API using the
  ECMWF, ICON, GFS, and AIFS models;
- calculates expected solar array generation with a temperature adjustment;
- stores source forecasts, generation calculations, actual temperature, model quality
  metrics, and actual XT60 power in PostgreSQL;
- selects a model by MAE/RMSE over a rolling window and uses an equal-weight ensemble
  when there is insufficient history;
- calculates and, when control is enabled, applies Backup Reserve using a linear policy;
- verifies BLE authentication, data freshness, bounds, the persistent rate limit,
  manual override, idempotency, and independent readback;
- writes decisions and control outcomes to a local JSONL audit log;
- displays sanitized status in a local dashboard in Russian, Ukrainian, and English;
- stores the actual input power of both XT60 ports every minute;
- sends the outcome of every scheduled and manual forecast run to Telegram, including
  partially available data when an error occurs.

## Architecture

```text
Open-Meteo (keyless HTTPS: GTI + temperature)
             |
             v
EcoFlow.EnergyManager (.NET 8 / ASP.NET Core / BackgroundService)
  |          |--> PostgreSQL: forecasts, MAE/RMSE, XT60 actuals
  |          |--> SolarCalculator -> policy -> guards -> audit
  |          '--> local Telegram sender
  |
  '-- HTTP http://127.0.0.1:8765
             |
             v
ecoflow-local (Python 3 / ha-ef-ble / BlueZ)
             |
             v
Bluetooth LE -> EcoFlow DELTA 2 Max
```

The dashboard and its API listen only on `http://127.0.0.1:5095` by default. The BLE
bridge listens only on `http://127.0.0.1:8765`.

> **Important:** the bridge source code is not included in this Git repository. It is a
> separate local infrastructure adapter, `projects/ecoflow-local`, deployed on the Linux
> host alongside the application. This repository contains only the .NET client for its
> verified API and contract tests.

## Run modes

### Normal mode

The service continuously runs three background tasks:

- forecast and calculation for the next day — daily at **23:00 Europe/Kyiv**;
- collection of the actual Open-Meteo Best Match temperature — at startup and at the
  5th minute of every hour;
- collection of actual XT60 power — once per minute.

The forecast hour and IANA time zone are configurable. Daylight-saving time transitions
are calculated using the system time-zone database.

### Manual one-shot

```bash
dotnet run --project src/EcoFlow.EnergyManager -- --once
```

`--once` performs the same forecast, model selection, calculation, guarded control,
audit, and Telegram outcome, then exits. A manual run does **not** automatically disable
the persistent rate limit. For an authorized unscheduled run, safe environment overrides
may be passed temporarily to the process without changing the persistent configuration.

Separate one-time collection of the actual temperature:

```bash
dotnet run --project src/EcoFlow.EnergyManager -- --collect-actual
```

## Solar irradiance forecast and model selection

The application calls the keyless Open-Meteo Forecast API with the array coordinates,
tilt, and azimuth and retrieves the following for tomorrow:

- `global_tilted_irradiance` (GTI), W/m²;
- `temperature_2m`, °C;
- separate series from the `ecmwf_ifs025`, `icon_seamless`, `gfs_seamless`, and
  `ecmwf_aifs025_single` models.

Hourly values and total generation are stored for each model. The actual Open-Meteo Best
Match temperature is matched against previously stored forecasts. MAE and RMSE are
calculated over the last 60 days. A model becomes eligible for automatic selection after
24 matched observations; the lowest MAE wins, followed by the lowest RMSE. Until enough
history has accumulated, an equal-weight average of all complete models is used. A
forecast with at least 20 hourly values is considered complete.

### Generation calculation

For each hour:

```text
T_panel = T_air + GTI * 0.03
K_temp  = clamp(1 + K_coefficient * (T_panel - 25), 0.5, 1.1)
E_hour  = P_nominal * (GTI / 1000) * K_temp * system_efficiency
```

Where:

- `T_air` — air temperature, °C;
- `T_panel` — estimated panel temperature, °C;
- `GTI` — irradiance on the tilted surface, W/m²;
- `K_coefficient` — temperature coefficient, `-0.004/°C` by default;
- `P_nominal` — nominal power, `1.0 kW` by default;
- `system_efficiency` — total system efficiency, `0.85` by default;
- `E_hour` — energy for the hour, kWh.

The daily forecast is the sum of `E_hour`. Base configuration: Odesa
(`46.4775, 30.7326`), 1 kW panels, 45° tilt, and Open-Meteo south azimuth `0°`.

## Backup Reserve policy

Let `E` be tomorrow's expected generation in kWh:

```text
E <= 1.0  -> 100%
E >= 6.0  -> 20%
otherwise -> round(116 - 16 * E), MidpointRounding.AwayFromZero
```

The result is additionally constrained to the safe range `20..100%`.

| E, kWh | Backup Reserve |
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

**Backup Reserve is not the upper charge limit.** Backup Reserve determines the portion
of the battery reserved for backup power/energy management, while the upper charge limit
caps the maximum SOC. The automation changes only Backup Reserve. The upper charge limit,
lower charge limit, and AC/DC outputs are available to the application only as status and
are not changed by this policy.

## Control safeguards

The source configuration ships with `ECOFLOW_CONTROL_ENABLED=false`. The following are
checked before issuing a command:

1. BLE is connected and authenticated;
2. the status and forecast are fresh;
3. Backup Reserve is available and enabled;
4. the target is an integer within the permitted bounds;
5. the manual override file is absent;
6. the persistent minimum interval has elapsed since the last write that was actually
   applied;
7. idempotency — a matching value is not written again.

Commands are serialized. The bridge performs its own model/authentication/freshness/bounds
checks, uses the verified `ha-ef-ble` method
`set_energy_backup_battery_level`, and waits for the
`energy_backup_battery_level` readback. .NET then performs a separate status read. A write
is recorded as successful only after both confirmations. Rate-limit state and the JSONL
audit log are stored outside the repository.

To temporarily block automatic writes without stopping monitoring:

```bash
touch ~/.local/share/ecoflow-energy-manager/manual-override
```

To resume:

```bash
rm ~/.local/share/ecoflow-energy-manager/manual-override
```

## Actual solar generation

The bridge returns the power of XT60 input 1, XT60 input 2, and their sum. Every minute,
`PvActualWorker` accepts only authenticated, fresh, and physically plausible values
(`0..2500 W`) and stores a sample in `solar_actual_samples` with the source
`ecoflow_ble_xt60`. This measures input power, not already-integrated daily energy.

## PostgreSQL

`ECOFLOW_POSTGRES_CONNECTION` is required. The application creates the schema
automatically:

- `weather_forecast_runs`;
- `weather_forecast_hourly`;
- `solar_generation_forecasts`;
- `weather_actual_hourly`;
- `weather_model_accuracy_snapshots`;
- `solar_actual_samples`.

The recommended local connection uses a Unix socket and peer authentication. No database
password is required in the repository.

## Dashboard and API

The local dashboard shows connection/authentication, SOC, input/output/grid power, XT60,
AC and 12 V, read-only charge limits, Backup Reserve, sample time, and stale/error status.

Local endpoints:

- `GET /api/status` — sanitized status;
- `GET /api/settings` — safe runtime settings;
- `PUT /api/settings` — validation, atomic persistence, and application of settings
  without a restart.

The API does not return the User ID, serial number, bridge URL, database connection
string, or raw BLE errors. The web interface provides no device-control write endpoint.
Editable settings include coordinates, time zone, panel/calculation parameters, run hour,
and freshness limits.

## Telegram outcome

The configured local sender is called after **every** scheduled forecast run and every
`--once` run. The message includes:

- forecast date;
- selected model;
- expected generation;
- Backup Reserve before the run, calculated target, and confirmed readback;
- overall success or error.

On forecast/control failure, fields available at the time of the error are sent. The
sender invocation has a timeout; the standard sender performs bounded retries. A delivery
failure is diagnosed but does not reverse or mask control that has already been completed
safely. The sender command receives the message as a separate argument without shell
interpolation.

The token, chat ID, and other Telegram data are stored only in the sender's protected
local configuration outside the repository. Do not add them to the environment example,
unit, launch arguments, or logs.

## Configuration

Secret-free example: `deploy/energy-manager.env.example`.

| Variable | Purpose |
|---|---|
| `ECOFLOW_LATITUDE`, `ECOFLOW_LONGITUDE` | array coordinates |
| `ECOFLOW_TIMEZONE` | IANA time zone |
| `ECOFLOW_NOMINAL_POWER_KW` | nominal panel power |
| `ECOFLOW_PANEL_TILT`, `ECOFLOW_PANEL_AZIMUTH` | array geometry |
| `ECOFLOW_SYSTEM_EFFICIENCY` | system efficiency |
| `ECOFLOW_TEMPERATURE_COEFFICIENT` | temperature coefficient |
| `ECOFLOW_FORECAST_RUN_HOUR` | local hour for the scheduled calculation |
| `ECOFLOW_WEATHER_MODELS` | list of Open-Meteo models |
| `ECOFLOW_WEATHER_RETRY_INTERVAL_SECONDS` | delay between failed weather requests; default 60 seconds |
| `ECOFLOW_WEATHER_RETRY_WINDOW_MINUTES` | maximum weather retry period; default 60 minutes |
| `ECOFLOW_ACCURACY_WINDOW_DAYS` | model evaluation window |
| `ECOFLOW_MIN_ACCURACY_SAMPLES` | minimum number of observations for model selection |
| `ECOFLOW_POSTGRES_CONNECTION` | PostgreSQL connection string |
| `ECOFLOW_MAX_STATUS_AGE_MINUTES` | maximum permitted BLE status age |
| `ECOFLOW_MAX_FORECAST_AGE_MINUTES` | maximum permitted forecast age |
| `ECOFLOW_CONTROL_ENABLED` | enables actual reserve control |
| `ECOFLOW_CONTROL_MIN_BACKUP_RESERVE`, `ECOFLOW_CONTROL_MAX_BACKUP_RESERVE` | safety bounds |
| `ECOFLOW_CONTROL_MIN_INTERVAL_MINUTES` | persistent rate limit |
| `ECOFLOW_MANUAL_OVERRIDE_FILE` | file that blocks automatic control |
| `ECOFLOW_CONTROL_STATE_PATH` | state of the last write |
| `ECOFLOW_DECISION_LOG` | JSONL audit log |
| `ECOFLOW_BRIDGE_URL` | local BLE bridge URL |
| `OPEN_METEO_URL` | Open-Meteo endpoint |
| `ECOFLOW_WEB_URLS` | ASP.NET Core listener address |
| `ECOFLOW_SETTINGS_PATH` | runtime settings JSON |
| `ECOFLOW_TELEGRAM_COMMAND` | absolute path to the local sender |
| `ECOFLOW_TELEGRAM_TIMEOUT_SECONDS` | overall sender timeout |

Secrets and local identifiers must not be stored in Git:

- EcoFlow User ID and BLE authentication state;
- device serial number;
- passwords/connection strings containing passwords;
- Telegram token/chat ID;
- local runtime state, audit logs, and settings.

## Build, run, and tests

Requirements:

- Linux with BlueZ and a Bluetooth controller — for the bridge;
- .NET SDK 8;
- Python 3 and a compatible version of `ha-ef-ble` — for the external bridge;
- PostgreSQL;
- access to the keyless Open-Meteo API.

```bash
dotnet restore EcoFlow.EnergyManager.sln
dotnet build EcoFlow.EnergyManager.sln -c Release
dotnet test EcoFlow.EnergyManager.sln -c Release
```

For development, open `EcoFlow.EnergyManager.sln` in Visual Studio 2022+.

## Deployment with systemd

The repository contains:

- `deploy/ecoflow-energy-manager.service` — user unit;
- `deploy/ecoflow-energy-manager-control.conf` — reviewed drop-in for active control;
- `deploy/energy-manager.env.example` — safe environment template.

Typical sequence on the target Linux host:

```bash
dotnet build EcoFlow.EnergyManager.sln -c Release
install -d ~/.config/systemd/user/ecoflow-energy-manager.service.d ~/.config/ecoflow
install -m 0644 deploy/ecoflow-energy-manager.service ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now ecoflow-ble-bridge.service
systemctl --user enable --now ecoflow-energy-manager.service
```

The environment file is created manually outside the repository with mode `0600`. Before
enabling control, verify the bridge, device model, bounds, manual override, and readback.
Only after that verification may the active drop-in be installed separately:

```bash
install -m 0644 deploy/ecoflow-energy-manager-control.conf ~/.config/systemd/user/ecoflow-energy-manager.service.d/control.conf
systemctl --user daemon-reload
systemctl --user restart ecoflow-energy-manager.service
```

Verification:

```bash
systemctl --user status ecoflow-ble-bridge.service ecoflow-energy-manager.service --no-pager
curl -fsS http://127.0.0.1:8765/health
curl -fsS http://127.0.0.1:5095/api/status
journalctl --user -u ecoflow-energy-manager.service --no-pager -n 100
```

## Troubleshooting

- **BLE does not connect:** close/disconnect the EcoFlow phone app — the device permits
  only one active BLE connection; then check BlueZ and the bridge journal.
- **Control blocked / stale:** check the `sampled_utc` time, bridge
  connectivity/authentication, and freshness limits.
- **Control rate limit is active:** wait for the interval to elapse. Do not delete state
  to bypass the normal restriction; for an authorized one-shot, use only a temporary
  process-level override.
- **Manual override is active:** make sure the block should actually be removed, then
  delete only the documented marker file.
- **Open-Meteo error/incomplete day:** the forecast run retries once per minute for up
  to one hour before reporting failure. If all attempts fail, check the network,
  endpoint, model list, and the presence of at least 20 samples.
- **PostgreSQL error:** check database availability, peer/user permissions, and the
  environment.
- **Telegram failed:** check the local sender and its protected environment; reserve
  control is not rolled back because of a Telegram error.
- **Readback mismatch:** do not repeat commands blindly; check the bridge journal, fresh
  status, and the absence of a second BLE client.

## Technology stack

- **.NET 8 / C#**;
- **ASP.NET Core**, Minimal API, static HTML/CSS/JavaScript dashboard;
- **BackgroundService**, **HttpClient**, **System.Text.Json**;
- **Npgsql / PostgreSQL**;
- **xUnit**;
- **Python 3**, **ha-ef-ble**, **BlueZ**, **Bluetooth LE** — separate local bridge;
- **Open-Meteo** keyless forecast/current APIs;
- **systemd user services**;
- local OpenClaw **Telegram sender**;
- **Linux**.

## Limitations and safety

- The solution is designed for the verified DELTA 2 Max model and a local single-device
  bridge.
- The dashboard and bridge must remain localhost-only.
- Do not publish the User ID, serial number, tokens, authentication/session material, or
  local state files.
- Do not substitute lower/upper charge limits for Backup Reserve.
- The automation does not control AC/DC outputs.
- XT60 measurements are currently stored as power samples; integration into actual daily
  energy and automatic loss calibration are not claimed.
