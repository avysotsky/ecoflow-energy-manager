namespace EcoFlow.EnergyManager;

public sealed record ModelWeatherForecast
{
    public required string Model { get; init; }
    public required SolarWeatherForecast Weather { get; init; }
}

public sealed record ModelSolarForecast
{
    public required string Model { get; init; }
    public required SolarForecast Forecast { get; init; }
}
