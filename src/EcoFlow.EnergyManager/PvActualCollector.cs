using Microsoft.Extensions.Logging;

namespace EcoFlow.EnergyManager;

public sealed class PvActualCollector(
    IEcoFlowGateway gateway,
    IPvDataStore dataStore,
    IRuntimeSettingsProvider settingsProvider,
    TimeProvider timeProvider,
    ILogger<PvActualCollector> logger)
{
    private const double MaximumPlausibleSolarPowerW = 2500;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await gateway.GetStatusAsync(cancellationToken);
            var age = timeProvider.GetUtcNow() - status.SampledUtc;
            if (!status.Connected || !status.Authenticated ||
                status.SampledUtc == default || age < TimeSpan.Zero ||
                age > settingsProvider.Current.MaximumStatusAge)
            {
                logger.LogWarning("Skipped PV sample because BLE status is unavailable or stale");
                return 1;
            }

            if (status.SolarInputPowerW is not double solarPower ||
                solarPower < 0 || solarPower > MaximumPlausibleSolarPowerW)
            {
                logger.LogWarning("Skipped PV sample because solar input power is unavailable or invalid");
                return 1;
            }

            await dataStore.SavePvPowerObservationAsync(new PvPowerObservation
            {
                SampledUtc = status.SampledUtc,
                Source = "ecoflow_ble_xt60",
                SolarInputPowerW = solarPower,
                Xt60Input1PowerW = status.Xt60Input1PowerW,
                Xt60Input2PowerW = status.Xt60Input2PowerW,
            }, cancellationToken);

            logger.LogInformation(
                "Stored actual PV input {SolarPower:F0} W for {SampledUtc:O}",
                solarPower,
                status.SampledUtc);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Actual PV collection failed");
            return 1;
        }
    }
}
