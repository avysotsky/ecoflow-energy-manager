using System.Net.Http.Json;

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
}
