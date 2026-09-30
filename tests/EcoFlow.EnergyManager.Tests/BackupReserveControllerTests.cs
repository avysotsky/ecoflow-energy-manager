using Xunit;

namespace EcoFlow.EnergyManager.Tests;

public sealed class BackupReserveControllerTests
{
    private static readonly DateTimeOffset Now = new(
        2026, 9, 30, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyAsync_AppliesAndIndependentlyVerifiesReserve()
    {
        var gateway = new FakeGateway(CreateStatus(80));
        var state = new FakeStateStore();
        var outcome = await CreateController(gateway, state).ApplyAsync(
            CreateDecision(80), CreateStatus(50), Now);

        Assert.Equal("applied", outcome.State);
        Assert.Equal(80, gateway.RequestedReserve);
        Assert.Equal(80, outcome.ReadbackBackupReserve);
        Assert.Equal(Now, state.LastAppliedUtc);
    }

    [Fact]
    public async Task ApplyAsync_IsIdempotentBeforeRateLimitCheck()
    {
        var gateway = new FakeGateway(CreateStatus(80));
        var state = new FakeStateStore { LastAppliedUtc = Now.AddMinutes(-1) };
        var outcome = await CreateController(gateway, state).ApplyAsync(
            CreateDecision(80), CreateStatus(80), Now);

        Assert.Equal("idempotent", outcome.State);
        Assert.Null(gateway.RequestedReserve);
    }

    [Fact]
    public async Task ApplyAsync_BlocksRecentDifferentWrite()
    {
        var gateway = new FakeGateway(CreateStatus(80));
        var state = new FakeStateStore { LastAppliedUtc = Now.AddMinutes(-10) };
        var outcome = await CreateController(gateway, state).ApplyAsync(
            CreateDecision(80), CreateStatus(60), Now);

        Assert.Equal("blocked", outcome.State);
        Assert.Contains("rate limit", outcome.Reason);
        Assert.Null(gateway.RequestedReserve);
    }

    [Fact]
    public async Task ApplyAsync_BlocksTargetOutsideHardBounds()
    {
        var gateway = new FakeGateway(CreateStatus(10));
        var outcome = await CreateController(gateway, new FakeStateStore()).ApplyAsync(
            CreateDecision(10), CreateStatus(50), Now);

        Assert.Equal("blocked", outcome.State);
        Assert.Contains("safety bounds", outcome.Reason);
        Assert.Null(gateway.RequestedReserve);
    }

    [Fact]
    public async Task ApplyAsync_BlocksWhenManualOverrideFileExists()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ecoflow-control-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var overridePath = Path.Combine(directory, "manual-override");
        await File.WriteAllTextAsync(overridePath, "manual");
        try
        {
            var options = CreateOptions() with { ManualOverrideFile = overridePath };
            var gateway = new FakeGateway(CreateStatus(80));
            var controller = new BackupReserveController(
                gateway,
                new FixedRuntimeSettingsProvider(options),
                new FakeStateStore());

            var outcome = await controller.ApplyAsync(
                CreateDecision(80), CreateStatus(50), Now);

            Assert.Equal("blocked", outcome.State);
            Assert.Contains("Manual override", outcome.Reason);
            Assert.Null(gateway.RequestedReserve);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static BackupReserveController CreateController(
        IEcoFlowGateway gateway,
        IBackupReserveControlStateStore stateStore) => new(
            gateway,
            new FixedRuntimeSettingsProvider(CreateOptions()),
            stateStore);

    private static EnergyManagerOptions CreateOptions() => new()
    {
        ControlEnabled = true,
        MinimumAllowedBackupReserve = 20,
        MaximumAllowedBackupReserve = 100,
        MinimumControlInterval = TimeSpan.FromHours(1),
        MaximumStatusAge = TimeSpan.FromMinutes(2),
        ManualOverrideFile = Path.Combine(
            Path.GetTempPath(),
            $"missing-manual-override-{Guid.NewGuid():N}"),
    };

    private static EnergyDecision CreateDecision(int target) => new()
    {
        DecidedUtc = Now,
        DryRun = false,
        IsActionable = true,
        RecommendedBackupReserve = target,
        Reason = "test",
    };

    private static EcoFlowStatus CreateStatus(double reserve) => new()
    {
        Connected = true,
        Authenticated = true,
        SampledUtc = Now.AddSeconds(-5),
        BackupReserveEnabled = true,
        BackupReserve = reserve,
        ChargeLimitMin = 0,
        ChargeLimitMax = 100,
    };

    private sealed class FakeGateway(EcoFlowStatus postflightStatus) : IEcoFlowGateway
    {
        public int? RequestedReserve { get; private set; }

        public Task<EcoFlowStatus> GetStatusAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(postflightStatus);

        public Task<BackupReserveWriteResult> SetBackupReserveAsync(
            int backupReserve,
            CancellationToken cancellationToken = default)
        {
            RequestedReserve = backupReserve;
            return Task.FromResult(new BackupReserveWriteResult
            {
                RequestedBackupReserve = backupReserve,
                PreviousBackupReserve = 50,
                ReadbackBackupReserve = backupReserve,
                BackupReserveEnabled = true,
                Applied = true,
                Idempotent = false,
            });
        }
    }

    private sealed class FakeStateStore : IBackupReserveControlStateStore
    {
        public DateTimeOffset? LastAppliedUtc { get; set; }

        public Task<DateTimeOffset?> GetLastAppliedUtcAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(LastAppliedUtc);

        public Task SaveLastAppliedUtcAsync(
            DateTimeOffset appliedUtc,
            CancellationToken cancellationToken = default)
        {
            LastAppliedUtc = appliedUtc;
            return Task.CompletedTask;
        }
    }
}
