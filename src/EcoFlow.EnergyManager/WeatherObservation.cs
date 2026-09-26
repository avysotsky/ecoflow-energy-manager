namespace EcoFlow.EnergyManager;

public sealed record WeatherObservation
{
    public required DateTimeOffset ObservedUtc { get; init; }
    public required double AirTemperatureCelsius { get; init; }
    public required string Source { get; init; }
}

public interface IActualWeatherProvider
{
    Task<WeatherObservation> GetCurrentAsync(
        CancellationToken cancellationToken = default);
}
