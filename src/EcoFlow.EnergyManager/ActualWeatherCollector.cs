using Microsoft.Extensions.Logging;

namespace EcoFlow.EnergyManager;

public sealed class ActualWeatherCollector(
    IActualWeatherProvider provider,
    IWeatherDataStore dataStore,
    ILogger<ActualWeatherCollector> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var observation = await provider.GetCurrentAsync(cancellationToken);
            await dataStore.SaveActualObservationAsync(observation, cancellationToken);
            logger.LogInformation(
                "Stored actual temperature {Temperature:F1}°C for {ObservedUtc:O} from {Source}",
                observation.AirTemperatureCelsius,
                observation.ObservedUtc,
                observation.Source);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Actual weather collection failed");
            return 1;
        }
    }
}
