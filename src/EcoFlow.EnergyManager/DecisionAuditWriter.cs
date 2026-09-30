using System.Text.Json;
using System.Text.Json.Serialization;

namespace EcoFlow.EnergyManager;

public sealed class DecisionAuditWriter(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task AppendAsync(
        EcoFlowStatus status,
        SolarForecast forecast,
        EnergyDecision decision,
        BackupReserveControlOutcome control,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException(
            "Decision log path has no parent directory.");
        Directory.CreateDirectory(directory);

        var record = new AuditRecord
        {
            Status = status,
            Forecast = forecast,
            Decision = decision,
            Control = control,
        };
        var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
        await File.AppendAllTextAsync(fullPath, line, cancellationToken);
    }

    private sealed record AuditRecord
    {
        public required EcoFlowStatus Status { get; init; }
        public required SolarForecast Forecast { get; init; }
        public required EnergyDecision Decision { get; init; }
        public required BackupReserveControlOutcome Control { get; init; }
    }
}
