namespace EcoFlow.EnergyManager;

public sealed record WeatherModelAccuracy
{
    public required string Model { get; init; }
    public required int SampleCount { get; init; }
    public required double MeanAbsoluteErrorCelsius { get; init; }
    public required double RootMeanSquareErrorCelsius { get; init; }
}

public sealed record ForecastSelection
{
    public required string Source { get; init; }
    public required SolarWeatherForecast Weather { get; init; }
    public WeatherModelAccuracy? Accuracy { get; init; }
}
