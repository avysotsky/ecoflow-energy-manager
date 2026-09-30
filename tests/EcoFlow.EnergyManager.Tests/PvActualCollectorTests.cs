using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EcoFlow.EnergyManager.Tests;

public sealed class PvActualCollectorTests
{
    [Fact]
    public async Task RunAsync_StoresFreshAuthenticatedSolarSample()
    {
        var sampledUtc = DateTimeOffset.UtcNow;
        var store = new RecordingPvDataStore();
        var collector = CreateCollector(new EcoFlowStatus
        {
            Connected = true,
            Authenticated = true,
            SampledUtc = sampledUtc,
            Xt60Input1PowerW = 120,
            Xt60Input2PowerW = 80,
            SolarInputPowerW = 200,
        }, store);

        var exitCode = await collector.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(store.Observation);
        Assert.Equal(sampledUtc, store.Observation.SampledUtc);
        Assert.Equal(200, store.Observation.SolarInputPowerW);
        Assert.Equal(120, store.Observation.Xt60Input1PowerW);
        Assert.Equal(80, store.Observation.Xt60Input2PowerW);
    }

    [Fact]
    public async Task RunAsync_DoesNotStoreStaleSample()
    {
        var store = new RecordingPvDataStore();
        var collector = CreateCollector(new EcoFlowStatus
        {
            Connected = true,
            Authenticated = true,
            SampledUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            SolarInputPowerW = 200,
        }, store);

        var exitCode = await collector.RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Null(store.Observation);
    }

    private static PvActualCollector CreateCollector(
        EcoFlowStatus status,
        RecordingPvDataStore store) => new(
            new FixedEcoFlowGateway(status),
            store,
            new FixedRuntimeSettingsProvider(new EnergyManagerOptions
            {
                MaximumStatusAge = TimeSpan.FromMinutes(2),
            }),
            TimeProvider.System,
            NullLogger<PvActualCollector>.Instance);

    private sealed class FixedEcoFlowGateway(EcoFlowStatus status) : IEcoFlowGateway
    {
        public Task<EcoFlowStatus> GetStatusAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(status);

        public Task<BackupReserveWriteResult> SetBackupReserveAsync(
            int backupReserve,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingPvDataStore : IPvDataStore
    {
        public PvPowerObservation? Observation { get; private set; }

        public Task SavePvPowerObservationAsync(
            PvPowerObservation observation,
            CancellationToken cancellationToken = default)
        {
            Observation = observation;
            return Task.CompletedTask;
        }
    }
}
