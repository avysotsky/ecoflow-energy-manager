# EcoFlow Energy Manager

Cross-platform .NET 8 application for local EcoFlow energy management. The current
milestone reads an EcoFlow DELTA 2 Max through a localhost-only BLE bridge on Linux.

## Current functionality

- reads BLE connection/authentication state;
- reads battery level and input/output power;
- reads AC and 12 V output state;
- reads configured charge limits;
- uses no EcoFlow cloud connection during normal status reads.

## Open in Visual Studio

Open `EcoFlow.EnergyManager.sln` with Visual Studio 2022 or newer. Install the .NET 8
SDK if Visual Studio does not already include it.

## Run on openclaw-lenovo

```bash
/home/user/.dotnet/dotnet run --project /home/user/ecoflow-energy-manager/src/EcoFlow.EnergyManager
```

The Linux BLE bridge listens only on `127.0.0.1:8765`. Its URL can be overridden with
the `ECOFLOW_BRIDGE_URL` environment variable. Account credentials, EcoFlow User ID,
device serial number, and other private configuration are not stored in this repository.

## Planned milestones

1. Open-Meteo forecast provider.
2. Energy policy engine in dry-run mode.
3. Guarded charge-limit control.
4. Background service and structured decision log.
