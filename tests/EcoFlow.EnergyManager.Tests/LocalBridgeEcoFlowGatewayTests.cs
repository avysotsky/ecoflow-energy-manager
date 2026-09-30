using System.Net;
using System.Text;
using Xunit;

namespace EcoFlow.EnergyManager.Tests;

public sealed class LocalBridgeEcoFlowGatewayTests
{
    [Fact]
    public async Task SetBackupReserveAsync_SendsLengthDelimitedJson()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8765"),
        };
        var gateway = new LocalBridgeEcoFlowGateway(client);

        var result = await gateway.SetBackupReserveAsync(80);

        Assert.Equal(80, result.ReadbackBackupReserve);
        Assert.True(result.BackupReserveEnabled);
        Assert.True(handler.ContentLength > 0);
        Assert.Contains("\"backup_reserve\":80", handler.Body, StringComparison.Ordinal);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public long? ContentLength { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/backup-reserve", request.RequestUri?.AbsolutePath);
            Assert.NotNull(request.Content);
            ContentLength = request.Content.Headers.ContentLength;
            Body = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"requested_backup_reserve\":80," +
                    "\"previous_backup_reserve\":50," +
                    "\"readback_backup_reserve\":80," +
                    "\"backup_reserve_enabled\":true," +
                    "\"applied\":true,\"idempotent\":false}",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
