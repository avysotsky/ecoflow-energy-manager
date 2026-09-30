using System.Text.Json;

namespace EcoFlow.EnergyManager;

public sealed record BackupReserveControlOutcome
{
    public required DateTimeOffset CompletedUtc { get; init; }
    public required string State { get; init; }
    public int? RequestedBackupReserve { get; init; }
    public double? PreviousBackupReserve { get; init; }
    public double? ReadbackBackupReserve { get; init; }
    public required string Reason { get; init; }

    public bool IsSuccess => State is "applied" or "idempotent" or "dry_run";
}

public interface IBackupReserveControlStateStore
{
    Task<DateTimeOffset?> GetLastAppliedUtcAsync(CancellationToken cancellationToken = default);
    Task SaveLastAppliedUtcAsync(
        DateTimeOffset appliedUtc,
        CancellationToken cancellationToken = default);
}

public sealed class FileBackupReserveControlStateStore(string path)
    : IBackupReserveControlStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task<DateTimeOffset?> GetLastAppliedUtcAsync(
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(fullPath);
        var state = await JsonSerializer.DeserializeAsync<ControlState>(
            stream,
            JsonOptions,
            cancellationToken);
        return state?.LastAppliedUtc;
    }

    public async Task SaveLastAppliedUtcAsync(
        DateTimeOffset appliedUtc,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException(
            "Control state path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                new ControlState { LastAppliedUtc = appliedUtc },
                JsonOptions,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                temporaryPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        File.Move(temporaryPath, fullPath, true);
    }

    private sealed record ControlState
    {
        public required DateTimeOffset LastAppliedUtc { get; init; }
    }
}

public sealed class BackupReserveController(
    IEcoFlowGateway gateway,
    IRuntimeSettingsProvider settingsProvider,
    IBackupReserveControlStateStore stateStore)
{
    private readonly SemaphoreSlim _controlLock = new(1, 1);

    public async Task<BackupReserveControlOutcome> ApplyAsync(
        EnergyDecision decision,
        EcoFlowStatus preflightStatus,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        await _controlLock.WaitAsync(cancellationToken);
        try
        {
            return await ApplyLockedAsync(decision, preflightStatus, nowUtc, cancellationToken);
        }
        finally
        {
            _controlLock.Release();
        }
    }

    private async Task<BackupReserveControlOutcome> ApplyLockedAsync(
        EnergyDecision decision,
        EcoFlowStatus status,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var options = settingsProvider.Current;
        var target = decision.RecommendedBackupReserve;
        if (!decision.IsActionable || target is null)
        {
            return Outcome("blocked", nowUtc, target, status, decision.Reason);
        }
        if (!options.ControlEnabled)
        {
            return Outcome(
                "dry_run",
                nowUtc,
                target,
                status,
                "Control is disabled; no device command was sent.");
        }
        if (File.Exists(Path.GetFullPath(options.ManualOverrideFile)))
        {
            return Outcome(
                "blocked",
                nowUtc,
                target,
                status,
                "Manual override is active; automatic control is blocked.");
        }
        if (target < options.MinimumAllowedBackupReserve ||
            target > options.MaximumAllowedBackupReserve)
        {
            return Outcome(
                "blocked",
                nowUtc,
                target,
                status,
                "Requested backup reserve is outside configured safety bounds.");
        }

        var statusError = ValidateStatus(status, nowUtc, options);
        if (statusError is not null)
        {
            return Outcome("blocked", nowUtc, target, status, statusError);
        }
        if (status.BackupReserveEnabled is true && RoundPercent(status.BackupReserve) == target)
        {
            return Outcome(
                "idempotent",
                nowUtc,
                target,
                status,
                "Device already has the requested backup reserve.");
        }

        try
        {
            var lastAppliedUtc = await stateStore.GetLastAppliedUtcAsync(cancellationToken);
            if (lastAppliedUtc is not null)
            {
                var elapsed = nowUtc - lastAppliedUtc.Value;
                if (elapsed < TimeSpan.Zero || elapsed < options.MinimumControlInterval)
                {
                    return Outcome(
                        "blocked",
                        nowUtc,
                        target,
                        status,
                        "Control rate limit is active.");
                }
            }

            var bridgeResult = await gateway.SetBackupReserveAsync(target.Value, cancellationToken);
            if (bridgeResult.RequestedBackupReserve != target ||
                !bridgeResult.BackupReserveEnabled ||
                RoundPercent(bridgeResult.ReadbackBackupReserve) != target)
            {
                return Outcome(
                    "failed",
                    nowUtc,
                    target,
                    status,
                    "Bridge readback did not confirm the requested backup reserve.");
            }

            var postflightStatus = await gateway.GetStatusAsync(cancellationToken);
            var postflightError = ValidateStatus(postflightStatus, nowUtc, options);
            if (postflightError is not null ||
                postflightStatus.BackupReserveEnabled is not true ||
                RoundPercent(postflightStatus.BackupReserve) != target)
            {
                return Outcome(
                    "failed",
                    nowUtc,
                    target,
                    postflightStatus,
                    "Independent postflight readback did not confirm the requested backup reserve.");
            }

            if (bridgeResult.Applied)
            {
                await stateStore.SaveLastAppliedUtcAsync(nowUtc, cancellationToken);
            }
            return Outcome(
                bridgeResult.Applied ? "applied" : "idempotent",
                nowUtc,
                target,
                postflightStatus,
                bridgeResult.Applied
                    ? "Backup reserve applied and verified."
                    : "Bridge confirmed that the requested backup reserve was already set.",
                bridgeResult.PreviousBackupReserve);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            return Outcome(
                "failed",
                nowUtc,
                target,
                status,
                $"Control failed ({error.GetType().Name}).");
        }
    }

    private static string? ValidateStatus(
        EcoFlowStatus status,
        DateTimeOffset nowUtc,
        EnergyManagerOptions options)
    {
        if (!status.Connected || !status.Authenticated)
        {
            return "BLE status is not connected and authenticated.";
        }
        var age = nowUtc - status.SampledUtc;
        if (status.SampledUtc == default || age < TimeSpan.Zero || age > options.MaximumStatusAge)
        {
            return "BLE status is stale.";
        }
        if (status.BackupReserve is null || status.BackupReserveEnabled is null)
        {
            return "Device backup reserve is unavailable.";
        }
        return null;
    }

    private static int? RoundPercent(double? value) =>
        value is null ? null : (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);

    private static BackupReserveControlOutcome Outcome(
        string state,
        DateTimeOffset nowUtc,
        int? target,
        EcoFlowStatus status,
        string reason,
        double? previous = null) => new()
        {
            CompletedUtc = nowUtc,
            State = state,
            RequestedBackupReserve = target,
            PreviousBackupReserve = previous ?? status.BackupReserve,
            ReadbackBackupReserve = status.BackupReserve,
            Reason = reason,
        };
}
