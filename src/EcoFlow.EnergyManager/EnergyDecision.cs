namespace EcoFlow.EnergyManager;

public sealed record EnergyDecision
{
    public required DateTimeOffset DecidedUtc { get; init; }
    public required bool DryRun { get; init; }
    public required bool IsActionable { get; init; }
    public int? RecommendedBackupReserve { get; init; }
    public required string Reason { get; init; }
}
