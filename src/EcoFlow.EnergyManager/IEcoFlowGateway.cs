namespace EcoFlow.EnergyManager;

public interface IEcoFlowGateway
{
    Task<EcoFlowStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
