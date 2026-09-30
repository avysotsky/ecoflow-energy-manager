namespace EcoFlow.EnergyManager;

public sealed class DryRunEnergyPolicy(IRuntimeSettingsProvider settingsProvider) : IEnergyPolicy
{
    public DryRunEnergyPolicy(EnergyManagerOptions options)
        : this(new FixedRuntimeSettingsProvider(options))
    {
    }

    public EnergyDecision Evaluate(
        EcoFlowStatus status,
        SolarForecast forecast,
        DateTimeOffset nowUtc)
    {
        var options = settingsProvider.Current;
        if (!status.Connected || !status.Authenticated)
        {
            return Blocked(
                nowUtc,
                options.ControlEnabled,
                "BLE status is not connected and authenticated.");
        }

        var statusAge = nowUtc - status.SampledUtc;
        if (status.SampledUtc == default ||
            statusAge < TimeSpan.Zero ||
            statusAge > options.MaximumStatusAge)
        {
            return Blocked(
                nowUtc,
                options.ControlEnabled,
                $"BLE status is stale (age {FormatAge(statusAge)}; maximum {FormatAge(options.MaximumStatusAge)}).");
        }

        var forecastAge = nowUtc - forecast.FetchedUtc;
        if (forecast.FetchedUtc == default ||
            forecastAge < TimeSpan.Zero ||
            forecastAge > options.MaximumForecastAge)
        {
            return Blocked(
                nowUtc,
                options.ControlEnabled,
                $"Weather forecast is stale (age {FormatAge(forecastAge)}; maximum {FormatAge(options.MaximumForecastAge)}).");
        }

        if (forecast.HourlySamples < 20)
        {
            return Blocked(
                nowUtc,
                options.ControlEnabled,
                "Weather forecast has insufficient hourly coverage.");
        }

        var recommendedReserve = CalculateBackupReserve(forecast.ExpectedGenerationKwh);
        var currentReserve = status.BackupReserve is null
            ? "unknown"
            : $"{status.BackupReserve:0}%";

        return new EnergyDecision
        {
            DecidedUtc = nowUtc,
            DryRun = !options.ControlEnabled,
            IsActionable = true,
            RecommendedBackupReserve = recommendedReserve,
            Reason =
                $"Tomorrow expected PV generation {forecast.ExpectedGenerationKwh:0.00} kWh " +
                $"from {forecast.TotalTiltedIrradiationKwhM2:0.00} kWh/m² GTI; " +
                $"current backup reserve {currentReserve}.",
        };
    }

    private static EnergyDecision Blocked(
        DateTimeOffset nowUtc,
        bool controlEnabled,
        string reason) => new()
        {
            DecidedUtc = nowUtc,
            DryRun = !controlEnabled,
            IsActionable = false,
            RecommendedBackupReserve = null,
            Reason = reason,
        };

    public static int CalculateBackupReserve(double expectedGenerationKwh)
    {
        if (expectedGenerationKwh <= 1.0)
        {
            return 100;
        }
        if (expectedGenerationKwh >= 6.0)
        {
            return 20;
        }
        return (int)Math.Round(
            116 - 16 * expectedGenerationKwh,
            MidpointRounding.AwayFromZero);
    }

    private static string FormatAge(TimeSpan age) =>
        age < TimeSpan.Zero ? "future timestamp" : $"{age.TotalSeconds:0}s";
}
