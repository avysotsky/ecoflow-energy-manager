namespace EcoFlow.EnergyManager;

public sealed record PvPowerObservation
{
    public required DateTimeOffset SampledUtc { get; init; }
    public required string Source { get; init; }
    public required double SolarInputPowerW { get; init; }
    public double? Xt60Input1PowerW { get; init; }
    public double? Xt60Input2PowerW { get; init; }
}
