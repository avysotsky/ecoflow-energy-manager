# EcoFlow Energy Manager

Cross-platform .NET 8 Worker Service for local EcoFlow energy management. It reads an
EcoFlow DELTA 2 Max through a localhost-only BLE bridge, forecasts tomorrow's output
of a south-facing 1 kW solar array, stores weather history in PostgreSQL, compares
weather models with actual temperatures, and records a safe dry-run charge-limit
recommendation.

## Current functionality

- reads BLE connection/authentication state;
- reads battery level and input/output power;
- reads AC and 12 V output state;
- reads configured charge limits;
- obtains tomorrow's hourly GTI and air-temperature forecasts from Open-Meteo
  ECMWF, ICON, GFS, and AIFS models;
- stores every model's hourly forecast and calculated PV generation in PostgreSQL;
- stores actual hourly temperature from Open-Meteo Best Match Current Weather;
- calculates 60-day temperature MAE/RMSE for every model;
- uses the model with the lowest MAE after 24 matched actual samples, and an equal
  ensemble while history is insufficient;
- calculates hourly and daily PV generation with temperature and system-loss corrections;
- recommends an upper charge limit in dry-run mode;
- blocks recommendations when BLE or forecast data is stale;
- appends every decision to a local JSONL audit log;
- uses no EcoFlow cloud connection during normal status reads.
- never sends a control command to the device.

## Open in Visual Studio

Open `EcoFlow.EnergyManager.sln` with Visual Studio 2022 or newer. Install the .NET 8
SDK if Visual Studio does not already include it.

## Run on openclaw-lenovo

```bash
/home/user/.dotnet/dotnet run --project /home/user/ecoflow-energy-manager/src/EcoFlow.EnergyManager
```

The default process is a Worker that calculates the next-day forecast every day at
23:00 `Europe/Kyiv`. Run one calculation immediately with:

```bash
/home/user/.dotnet/dotnet run --project /home/user/ecoflow-energy-manager/src/EcoFlow.EnergyManager -- --once
```

The Linux BLE bridge listens only on `127.0.0.1:8765`. Its URL can be overridden with
the `ECOFLOW_BRIDGE_URL` environment variable. Account credentials, EcoFlow User ID,
device serial number, and other private configuration are not stored in this repository.

## PostgreSQL weather history

`ECOFLOW_POSTGRES_CONNECTION` is required. Keep it in the deployment host's private
environment file, never in source control:

```text
~/.config/ecoflow/energy-manager.env
```

The application creates and maintains these tables automatically:

- `weather_forecast_runs`;
- `weather_forecast_hourly`;
- `solar_generation_forecasts`;
- `weather_actual_hourly`;
- `weather_model_accuracy_snapshots`.

The background service records current actual temperature at five minutes past every
hour. The daily 23:00 run stores every available model forecast, recalculates model
accuracy, then selects the most accurate model for the dry-run decision. Run only the
actual-temperature collection with `--collect-actual`.

## Dry-run policy

Defaults follow `SolarForecast_Service.md`: Odesa coordinates (`46.4775`, `30.7326`),
`Europe/Kyiv`, 1 kW nominal power, 45° tilt, south azimuth `0°`, system efficiency
`0.85`, and temperature coefficient `-0.004/°C`.

For every hour the application calculates:

```text
panel temperature = air temperature + GTI × 0.03
temperature factor = 1 + coefficient × (panel temperature - 25)
generation = nominal kW × GTI/1000 × temperature factor × system efficiency
```

Tomorrow's expected PV generation determines the recommendation:

- at least `3.0 kWh`: upper charge limit `70%`;
- at least `1.5 kWh`: upper charge limit `85%`;
- below `1.5 kWh`: upper charge limit `100%`.

All values are configurable with environment variables:

- `ECOFLOW_LATITUDE`, `ECOFLOW_LONGITUDE`, `ECOFLOW_TIMEZONE`;
- `ECOFLOW_NOMINAL_POWER_KW`, `ECOFLOW_PANEL_TILT`, `ECOFLOW_PANEL_AZIMUTH`;
- `ECOFLOW_SYSTEM_EFFICIENCY`, `ECOFLOW_TEMPERATURE_COEFFICIENT`;
- `ECOFLOW_FORECAST_RUN_HOUR`;
- `ECOFLOW_WEATHER_MODELS`, `ECOFLOW_ACCURACY_WINDOW_DAYS`;
- `ECOFLOW_MIN_ACCURACY_SAMPLES`, `ECOFLOW_POSTGRES_CONNECTION`;
- `ECOFLOW_MODERATE_GENERATION_KWH`, `ECOFLOW_HIGH_GENERATION_KWH`;
- `ECOFLOW_HIGH_SOLAR_LIMIT`, `ECOFLOW_MODERATE_SOLAR_LIMIT`, `ECOFLOW_LOW_SOLAR_LIMIT`;
- `ECOFLOW_MAX_STATUS_AGE_MINUTES`, `ECOFLOW_MAX_FORECAST_AGE_MINUTES`;
- `ECOFLOW_DECISION_LOG`, `OPEN_METEO_URL`.

The default audit log is stored outside the repository under the user's local
application-data directory.

## Planned milestones

1. Accumulate actual temperatures and validate the automatic model ranking.
2. Add an independent local temperature sensor or station observation as the reference.
3. Store actual PV production separately from other EcoFlow input power and use it to
   train generation accuracy and system-loss calibration.
4. Add guarded charge-limit control with hard bounds and manual override.
