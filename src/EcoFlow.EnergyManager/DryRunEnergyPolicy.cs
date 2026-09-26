namespace EcoFlow.EnergyManager;

public sealed class DryRunEnergyPolicy(EnergyManagerOptions options) : IEnergyPolicy
{
    public EnergyDecision Evaluate(
        EcoFlowStatus status,
        SolarForecast forecast,
        DateTimeOffset nowUtc)
    {
        if (!status.Connected || !status.Authenticated)
        {
            return Blocked(nowUtc, "BLE status is not connected and authenticated.");
        }

        var statusAge = nowUtc - status.SampledUtc;
        if (status.SampledUtc == default ||
            statusAge < TimeSpan.Zero ||
            statusAge > options.MaximumStatusAge)
        {
            return Blocked(
                nowUtc,
                $"BLE status is stale (age {FormatAge(statusAge)}; maximum {FormatAge(options.MaximumStatusAge)}).");
        }

        var forecastAge = nowUtc - forecast.FetchedUtc;
        if (forecast.FetchedUtc == default ||
            forecastAge < TimeSpan.Zero ||
            forecastAge > options.MaximumForecastAge)
        {
            return Blocked(
                nowUtc,
                $"Weather forecast is stale (age {FormatAge(forecastAge)}; maximum {FormatAge(options.MaximumForecastAge)}).");
        }

        if (forecast.HourlySamples < 20)
        {
            return Blocked(nowUtc, "Weather forecast has insufficient hourly coverage.");
        }

        var recommendedLimit = forecast.ExpectedGenerationKwh >= options.HighExpectedGenerationKwh
            ? options.HighSolarChargeLimit
            : forecast.ExpectedGenerationKwh >= options.ModerateExpectedGenerationKwh
                ? options.ModerateSolarChargeLimit
                : options.LowSolarChargeLimit;

        var currentLimit = status.ChargeLimitMax is null
            ? "unknown"
            : $"{status.ChargeLimitMax:0}%";

        return new EnergyDecision
        {
            DecidedUtc = nowUtc,
            DryRun = true,
            IsActionable = true,
            RecommendedUpperChargeLimit = recommendedLimit,
            Reason =
                $"Tomorrow expected PV generation {forecast.ExpectedGenerationKwh:0.00} kWh " +
                $"from {forecast.TotalTiltedIrradiationKwhM2:0.00} kWh/m² GTI; " +
                $"current upper limit {currentLimit}.",
        };
    }

    private static EnergyDecision Blocked(DateTimeOffset nowUtc, string reason) => new()
    {
        DecidedUtc = nowUtc,
        DryRun = true,
        IsActionable = false,
        RecommendedUpperChargeLimit = null,
        Reason = reason,
    };

    private static string FormatAge(TimeSpan age) =>
        age < TimeSpan.Zero ? "future timestamp" : $"{age.TotalSeconds:0}s";
}
