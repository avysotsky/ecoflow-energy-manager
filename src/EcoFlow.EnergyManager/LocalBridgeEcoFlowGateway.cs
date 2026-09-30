using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EcoFlow.EnergyManager;

public sealed class LocalBridgeEcoFlowGateway(HttpClient httpClient) : IEcoFlowGateway
{
    public async Task<EcoFlowStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var status = await httpClient.GetFromJsonAsync<EcoFlowStatus>(
            "/api/status",
            cancellationToken);
        return status ?? throw new InvalidOperationException(
            "EcoFlow BLE bridge returned an empty response.");
    }

    public async Task<BackupReserveWriteResult> SetBackupReserveAsync(
        int backupReserve,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(new { backup_reserve = backupReserve });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(
            "/api/backup-reserve",
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BackupReserveWriteResult>(
            cancellationToken);
        return result ?? throw new InvalidOperationException(
            "EcoFlow BLE bridge returned an empty backup-reserve response.");
    }
}
