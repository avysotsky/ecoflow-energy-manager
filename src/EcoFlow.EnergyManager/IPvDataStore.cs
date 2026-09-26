namespace EcoFlow.EnergyManager;

public interface IPvDataStore
{
    Task SavePvPowerObservationAsync(
        PvPowerObservation observation,
        CancellationToken cancellationToken = default);
}
