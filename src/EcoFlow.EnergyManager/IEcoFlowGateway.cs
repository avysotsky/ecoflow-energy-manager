namespace EcoFlow.EnergyManager;

public interface IEcoFlowGateway
{
    Task<EcoFlowStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<BackupReserveWriteResult> SetBackupReserveAsync(
        int backupReserve,
        CancellationToken cancellationToken = default);
}
