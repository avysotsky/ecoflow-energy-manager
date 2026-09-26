namespace EcoFlow.EnergyManager;

public sealed record SolarForecast
{
    public required DateOnly ForecastDate { get; init; }
    public required DateTimeOffset FetchedUtc { get; init; }
    public required double ExpectedGenerationKwh { get; init; }
    public required double TotalTiltedIrradiationKwhM2 { get; init; }
    public required int HourlySamples { get; init; }
    public required IReadOnlyList<HourlyGeneration> Hours { get; init; }
}

public sealed record HourlyGeneration
{
    public required DateTime LocalTime { get; init; }
    public required double GlobalTiltedIrradianceWm2 { get; init; }
    public required double AirTemperatureCelsius { get; init; }
    public required double PanelTemperatureCelsius { get; init; }
    public required double EnergyKwh { get; init; }
}
