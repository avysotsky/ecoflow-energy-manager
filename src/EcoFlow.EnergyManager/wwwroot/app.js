"use strict";

const byId = (id) => document.getElementById(id);
const form = byId("settings-form");
const translations = {
  ru: {
    language: "Язык", localDashboard: "Локальная панель", loading: "Загрузка…",
    currentStatus: "Текущее состояние", noData: "Нет данных", battery: "Батарея",
    input: "Вход", solarInput: "Солнечный вход", output: "Выход", balance: "Баланс", positiveMeansCharging: "«+» — заряд",
    bleBridge: "BLE-мост", connection: "Соединение", authentication: "Авторизация",
    ports: "AC / 12 В", chargeLimits: "Лимиты заряда", readOnly: "только чтение",
    backupReserve: "Резерв питания", automaticControl: "автоматическое управление",
    safeAutomaticControl: "Безопасное автоматическое управление", calculationSettings: "Настройки расчёта",
    settingsHint: "Сохраняются локально и применяются без перезапуска",
    locationAndSchedule: "Место и расписание", latitude: "Широта", longitude: "Долгота",
    timeZone: "Часовой пояс IANA", forecastHour: "Час прогноза",
    solarInstallation: "Солнечная установка", nominalPower: "Номинальная мощность, кВт",
    tilt: "Наклон, °", azimuth: "Азимут, °", efficiency: "Эффективность",
    temperatureCoefficient: "Темп. коэффициент /°C", linearPolicy: "Линейная политика резерва",
    linearPolicyDescription: "≤1 кВт·ч → 100%; ≥6 кВт·ч → 20%; между ними: 116 − 16 × кВт·ч.", acceptableFreshness: "Допустимая свежесть",
    bleStatusMinutes: "Статус BLE, минут", forecastMinutes: "Прогноз, минут",
    saveSettings: "Сохранить настройки",
    footer: "Автоматизация управляет только резервом питания. Лимиты заряда и выходы остаются без изменений.",
    on: "Включено", off: "Выключено", yes: "Да", no: "Нет", measurement: "Замер",
    noMeasurementTime: "Нет времени замера", error: "Ошибка", stale: "Данные устарели",
    current: "Данные актуальны", noConnection: "Нет связи",
    staleMessage: "Данные BLE-моста устарели.", appUnavailable: "Веб-приложение не отвечает.",
    bridgeError: "Мост сообщил об ошибке. Подробности доступны только в локальном журнале.",
    bridgeUnavailableMessage: "Не удалось получить состояние локального BLE-моста.",
    fixFields: "Исправьте отмеченные поля.", saving: "Сохранение…", saved: "Сохранено и применено.",
    saveFailed: "Не удалось сохранить настройки.", loadFailed: "Не удалось загрузить настройки.",
    watt: " Вт", bridgeConnected: "подключён", bridgeAuthenticated: "аутентифицирован",
    bridgeConnecting: "подключение", bridgeAuthenticating: "аутентификация",
    bridgeDisconnected: "не подключён", bridgeErrorState: "ошибка", bridgeUnavailable: "недоступен"
  },
  en: {
    language: "Language", localDashboard: "Local dashboard", loading: "Loading…",
    currentStatus: "Current status", noData: "No data", battery: "Battery",
    input: "Input", solarInput: "Solar input", output: "Output", balance: "Balance", positiveMeansCharging: "“+” means charging",
    bleBridge: "BLE bridge", connection: "Connection", authentication: "Authentication",
    ports: "AC / 12 V", chargeLimits: "Charge limits", readOnly: "read only",
    backupReserve: "Backup reserve", automaticControl: "automatic control",
    safeAutomaticControl: "Safe automatic control", calculationSettings: "Calculation settings",
    settingsHint: "Saved locally and applied without restart",
    locationAndSchedule: "Location and schedule", latitude: "Latitude", longitude: "Longitude",
    timeZone: "IANA time zone", forecastHour: "Forecast hour",
    solarInstallation: "Solar installation", nominalPower: "Nominal power, kW",
    tilt: "Tilt, °", azimuth: "Azimuth, °", efficiency: "Efficiency",
    temperatureCoefficient: "Temperature coefficient /°C", linearPolicy: "Linear reserve policy",
    linearPolicyDescription: "≤1 kWh → 100%; ≥6 kWh → 20%; between: 116 − 16 × kWh.", acceptableFreshness: "Acceptable freshness",
    bleStatusMinutes: "BLE status, minutes", forecastMinutes: "Forecast, minutes",
    saveSettings: "Save settings",
    footer: "Automation controls only backup reserve. Charge limits and outputs remain unchanged.",
    on: "On", off: "Off", yes: "Yes", no: "No", measurement: "Sampled",
    noMeasurementTime: "No sample time", error: "Error", stale: "Data is stale",
    current: "Data is current", noConnection: "No connection",
    staleMessage: "BLE bridge data is stale.", appUnavailable: "The web application is not responding.",
    bridgeError: "The bridge reported an error. Details are available only in the local log.",
    bridgeUnavailableMessage: "Unable to retrieve the local BLE bridge status.",
    fixFields: "Correct the highlighted fields.", saving: "Saving…", saved: "Saved and applied.",
    saveFailed: "Could not save settings.", loadFailed: "Could not load settings.",
    watt: " W", bridgeConnected: "connected", bridgeAuthenticated: "authenticated",
    bridgeConnecting: "connecting", bridgeAuthenticating: "authenticating",
    bridgeDisconnected: "disconnected", bridgeErrorState: "error", bridgeUnavailable: "unavailable"
  },
  uk: {
    language: "Мова", localDashboard: "Локальна панель", loading: "Завантаження…",
    currentStatus: "Поточний стан", noData: "Немає даних", battery: "Батарея",
    input: "Вхід", solarInput: "Сонячний вхід", output: "Вихід", balance: "Баланс", positiveMeansCharging: "«+» — заряджання",
    bleBridge: "BLE-міст", connection: "З’єднання", authentication: "Автентифікація",
    ports: "AC / 12 В", chargeLimits: "Межі заряду", readOnly: "лише читання",
    backupReserve: "Резерв живлення", automaticControl: "автоматичне керування",
    safeAutomaticControl: "Безпечне автоматичне керування", calculationSettings: "Налаштування розрахунку",
    settingsHint: "Зберігаються локально та застосовуються без перезапуску",
    locationAndSchedule: "Місце та розклад", latitude: "Широта", longitude: "Довгота",
    timeZone: "Часовий пояс IANA", forecastHour: "Година прогнозу",
    solarInstallation: "Сонячна установка", nominalPower: "Номінальна потужність, кВт",
    tilt: "Нахил, °", azimuth: "Азимут, °", efficiency: "Ефективність",
    temperatureCoefficient: "Температурний коефіцієнт /°C", linearPolicy: "Лінійна політика резерву",
    linearPolicyDescription: "≤1 кВт·год → 100%; ≥6 кВт·год → 20%; між ними: 116 − 16 × кВт·год.", acceptableFreshness: "Допустима актуальність",
    bleStatusMinutes: "Статус BLE, хвилин", forecastMinutes: "Прогноз, хвилин",
    saveSettings: "Зберегти налаштування",
    footer: "Автоматизація керує лише резервом живлення. Межі заряду та виходи не змінюються.",
    on: "Увімкнено", off: "Вимкнено", yes: "Так", no: "Ні", measurement: "Виміряно",
    noMeasurementTime: "Немає часу вимірювання", error: "Помилка", stale: "Дані застаріли",
    current: "Дані актуальні", noConnection: "Немає зв’язку",
    staleMessage: "Дані BLE-моста застаріли.", appUnavailable: "Вебзастосунок не відповідає.",
    bridgeError: "Міст повідомив про помилку. Подробиці доступні лише в локальному журналі.",
    bridgeUnavailableMessage: "Не вдалося отримати стан локального BLE-моста.",
    fixFields: "Виправте позначені поля.", saving: "Збереження…", saved: "Збережено та застосовано.",
    saveFailed: "Не вдалося зберегти налаштування.", loadFailed: "Не вдалося завантажити налаштування.",
    watt: " Вт", bridgeConnected: "підключено", bridgeAuthenticated: "автентифіковано",
    bridgeConnecting: "підключення", bridgeAuthenticating: "автентифікація",
    bridgeDisconnected: "не підключено", bridgeErrorState: "помилка", bridgeUnavailable: "недоступний"
  }
};
const savedLanguage = localStorage.getItem("ecoflow-language");
let language = Object.hasOwn(translations, savedLanguage) ? savedLanguage : "ru";
let lastStatus = null;
let statusRequestFailed = false;
let settingsResultKey = null;
const t = (key) => translations[language][key];
const numberFields = new Set([
  "latitude", "longitude", "nominalPowerKw", "panelTiltDegrees",
  "panelAzimuthDegrees", "systemEfficiency", "temperatureCoefficientPerCelsius",
  "forecastRunHourLocal",
  "maximumStatusAgeMinutes", "maximumForecastAgeMinutes"
]);

function formatNumber(value, suffix = "") {
  const locales = { ru: "ru-RU", uk: "uk-UA", en: "en-US" };
  return value === null || value === undefined ? "—" : `${new Intl.NumberFormat(locales[language], { maximumFractionDigits: 1 }).format(value)}${suffix}`;
}

function formatSwitch(value) {
  if (value === null || value === undefined) return "—";
  return value ? t("on") : t("off");
}

function formatBridgeState(value) {
  const states = {
    "подключён": "bridgeConnected", "аутентифицирован": "bridgeAuthenticated",
    "подключение": "bridgeConnecting", "аутентификация": "bridgeAuthenticating",
    "не подключён": "bridgeDisconnected", "ошибка": "bridgeErrorState", "недоступен": "bridgeUnavailable"
  };
  return states[value] ? t(states[value]) : value;
}

function formatErrorMessage(value) {
  if (!value) return t("staleMessage");
  if (value === translations.ru.bridgeError) return t("bridgeError");
  if (value === translations.ru.bridgeUnavailableMessage) return t("bridgeUnavailableMessage");
  return value;
}

function renderStatus(value) {
  byId("battery").textContent = formatNumber(value.batteryLevel, "%");
  byId("battery-bar").style.width = `${Math.max(0, Math.min(100, value.batteryLevel ?? 0))}%`;
  byId("input-power").textContent = formatNumber(value.inputPowerW, t("watt"));
  byId("solar-input-power").textContent = formatNumber(value.solarInputPowerW, t("watt"));
  byId("output-power").textContent = formatNumber(value.outputPowerW, t("watt"));
  byId("net-power").textContent = value.netPowerW === null || value.netPowerW === undefined
    ? "—"
    : `${value.netPowerW > 0 ? "+" : ""}${formatNumber(value.netPowerW, t("watt"))}`;
  byId("bridge").textContent = formatBridgeState(value.bridgeState);
  byId("connected").textContent = value.connected ? t("yes") : t("no");
  byId("authenticated").textContent = value.authenticated ? t("yes") : t("no");
  byId("ports").textContent = `${formatSwitch(value.acPorts)} / ${formatSwitch(value.dc12VPort)}`;
  byId("backup-reserve").textContent = `${formatNumber(value.backupReserve, "%")} / ${formatSwitch(value.backupReserveEnabled)}`;
  byId("charge-limits").textContent = `${formatNumber(value.chargeLimitMin, "%")} — ${formatNumber(value.chargeLimitMax, "%")}`;
  byId("sampled").textContent = value.sampledUtc
    ? `${t("measurement")}: ${new Date(value.sampledUtc).toLocaleString({ ru: "ru-RU", uk: "uk-UA", en: "en-US" }[language])}`
    : t("noMeasurementTime");

  const state = byId("overall-state");
  const alert = byId("status-alert");
  if (value.error || value.stale) {
    state.className = "state state-error";
    state.textContent = value.error ? t("error") : t("stale");
    alert.textContent = formatErrorMessage(value.errorMessage);
    alert.classList.remove("hidden");
  } else {
    state.className = "state state-ok";
    state.textContent = t("current");
    alert.classList.add("hidden");
  }
}

function renderRequestFailure() {
  const state = byId("overall-state");
  state.className = "state state-error";
  state.textContent = t("noConnection");
  const alert = byId("status-alert");
  alert.textContent = t("appUnavailable");
  alert.classList.remove("hidden");
}

function setLanguage(nextLanguage) {
  language = Object.hasOwn(translations, nextLanguage) ? nextLanguage : "ru";
  localStorage.setItem("ecoflow-language", language);
  document.documentElement.lang = language;
  document.querySelectorAll("[data-i18n]").forEach((element) => {
    element.textContent = t(element.dataset.i18n);
  });
  document.querySelectorAll("[data-i18n-aria-label]").forEach((element) => {
    element.setAttribute("aria-label", t(element.dataset.i18nAriaLabel));
  });
  document.querySelectorAll("[data-language]").forEach((button) => {
    button.setAttribute("aria-pressed", String(button.dataset.language === language));
  });
  if (lastStatus) renderStatus(lastStatus);
  else if (statusRequestFailed) renderRequestFailure();
  else byId("overall-state").textContent = t("loading");
  if (settingsResultKey) byId("settings-result").textContent = t(settingsResultKey);
}

function setSettingsResult(key) {
  settingsResultKey = key;
  byId("settings-result").textContent = t(key);
}

async function refreshStatus() {
  try {
    const response = await fetch("/api/status", { cache: "no-store" });
    if (!response.ok) throw new Error("status");
    const value = await response.json();
    lastStatus = value;
    statusRequestFailed = false;
    renderStatus(value);
  } catch {
    statusRequestFailed = true;
    renderRequestFailure();
  }
}

function clearErrors() {
  document.querySelectorAll("[data-error]").forEach((element) => { element.textContent = ""; });
}

function showErrors(errors) {
  clearErrors();
  for (const [name, messages] of Object.entries(errors || {})) {
    const target = document.querySelector(`[data-error="${name.charAt(0).toLowerCase()}${name.slice(1)}"]`);
    if (target) target.textContent = messages.join(" ");
  }
}

async function loadSettings() {
  const response = await fetch("/api/settings", { cache: "no-store" });
  if (!response.ok) throw new Error("settings");
  const values = await response.json();
  for (const [name, value] of Object.entries(values)) {
    if (form.elements[name]) form.elements[name].value = value;
  }
}

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  clearErrors();
  if (!form.checkValidity()) {
    form.reportValidity();
    setSettingsResult("fixFields");
    return;
  }
  const button = form.querySelector("button");
  button.disabled = true;
  setSettingsResult("saving");
  const payload = Object.fromEntries(new FormData(form).entries());
  for (const name of numberFields) payload[name] = Number(payload[name]);

  try {
    const response = await fetch("/api/settings", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });
    if (response.status === 400) {
      const problem = await response.json();
      showErrors(problem.errors);
      setSettingsResult("fixFields");
      return;
    }
    if (!response.ok) throw new Error("save");
    setSettingsResult("saved");
  } catch {
    setSettingsResult("saveFailed");
  } finally {
    button.disabled = false;
  }
});

document.querySelectorAll("[data-language]").forEach((button) => {
  button.addEventListener("click", () => setLanguage(button.dataset.language));
});

setLanguage(language);
loadSettings().catch(() => { setSettingsResult("loadFailed"); });
refreshStatus();
window.setInterval(refreshStatus, 2000);
