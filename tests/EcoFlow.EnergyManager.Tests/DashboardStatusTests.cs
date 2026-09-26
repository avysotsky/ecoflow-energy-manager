using System.Text.Json;
using Xunit;

namespace EcoFlow.EnergyManager.Tests;

public sealed class DashboardStatusTests
{
    [Fact]
    public void FromStatus_ComputesNetPowerAndDoesNotExposeModelOrRawError()
    {
        var result = DashboardStatus.FromStatus(new EcoFlowStatus
        {
            BridgeState = "connected",
            Connected = true,
            Authenticated = true,
            Model = "sensitive-device-value",
            InputPowerW = 500,
            OutputPowerW = 125,
            LastError = "sensitive-error-value",
            SampledUtc = DateTimeOffset.UtcNow,
        }, stale: false);

        var json = JsonSerializer.Serialize(result);

        Assert.Equal(375, result.NetPowerW);
        Assert.DoesNotContain("sensitive-device-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-error-value", json, StringComparison.Ordinal);
        Assert.True(result.Error);
    }
}
