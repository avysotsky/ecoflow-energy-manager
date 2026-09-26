using Xunit;

namespace EcoFlow.EnergyManager.Tests;

public sealed class DryRunEnergyPolicyTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        9,
        26,
        10,
        0,
        0,
        TimeSpan.Zero);

    [Theory]
    [InlineData(3.5, 70)]
    [InlineData(3.0, 70)]
    [InlineData(2.0, 85)]
    [InlineData(1.5, 85)]
    [InlineData(1.0, 100)]
    public void Evaluate_MapsExpectedGenerationToExpectedLimit(
        double expectedGenerationKwh,
        int expectedLimit)
    {
        var decision = CreatePolicy().Evaluate(
            CreateStatus(Now.AddSeconds(-10)),
            CreateForecast(Now.AddMinutes(-5), expectedGenerationKwh),
            Now);

        Assert.True(decision.DryRun);
        Assert.True(decision.IsActionable);
        Assert.Equal(expectedLimit, decision.RecommendedUpperChargeLimit);
    }

    [Fact]
    public void Evaluate_BlocksStaleStatus()
    {
        var decision = CreatePolicy().Evaluate(
            CreateStatus(Now.AddMinutes(-3)),
            CreateForecast(Now.AddMinutes(-5), 3.5),
            Now);

        Assert.False(decision.IsActionable);
        Assert.Null(decision.RecommendedUpperChargeLimit);
        Assert.Contains("BLE status is stale", decision.Reason);
    }

    [Fact]
    public void Evaluate_BlocksStaleForecast()
    {
        var decision = CreatePolicy().Evaluate(
            CreateStatus(Now.AddSeconds(-10)),
            CreateForecast(Now.AddHours(-2), 3.5),
            Now);

        Assert.False(decision.IsActionable);
        Assert.Null(decision.RecommendedUpperChargeLimit);
        Assert.Contains("Weather forecast is stale", decision.Reason);
    }

    [Fact]
    public void Evaluate_BlocksUnauthenticatedBridge()
    {
        var status = CreateStatus(Now.AddSeconds(-10)) with { Authenticated = false };

        var decision = CreatePolicy().Evaluate(
            status,
            CreateForecast(Now.AddMinutes(-5), 3.5),
            Now);

        Assert.False(decision.IsActionable);
        Assert.Null(decision.RecommendedUpperChargeLimit);
        Assert.Contains("not connected and authenticated", decision.Reason);
    }

    private static DryRunEnergyPolicy CreatePolicy() => new(new EnergyManagerOptions());

    private static EcoFlowStatus CreateStatus(DateTimeOffset sampledUtc) => new()
    {
        Connected = true,
        Authenticated = true,
        BatteryLevel = 80,
        ChargeLimitMax = 100,
        SampledUtc = sampledUtc,
    };

    private static SolarForecast CreateForecast(
        DateTimeOffset fetchedUtc,
        double expectedGenerationKwh) => new()
    {
        ForecastDate = new DateOnly(2026, 9, 27),
        FetchedUtc = fetchedUtc,
        ExpectedGenerationKwh = expectedGenerationKwh,
        TotalTiltedIrradiationKwhM2 = 4,
        HourlySamples = 24,
        Hours = [],
    };
}
