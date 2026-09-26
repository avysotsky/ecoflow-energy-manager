namespace EcoFlow.EnergyManager;

public sealed record SolarWeatherForecast
{
    public required DateOnly ForecastDate { get; init; }
    public required DateTimeOffset FetchedUtc { get; init; }
    public required IReadOnlyList<HourlySolarWeather> Hours { get; init; }
}

public sealed record HourlySolarWeather
{
    public required DateTime LocalTime { get; init; }
    public required double GlobalTiltedIrradianceWm2 { get; init; }
    public required double AirTemperatureCelsius { get; init; }
}
