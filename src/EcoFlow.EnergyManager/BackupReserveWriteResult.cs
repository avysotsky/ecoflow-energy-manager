using System.Text.Json.Serialization;

namespace EcoFlow.EnergyManager;

public sealed record BackupReserveWriteResult
{
    [JsonPropertyName("requested_backup_reserve")]
    public int RequestedBackupReserve { get; init; }

    [JsonPropertyName("previous_backup_reserve")]
    public double PreviousBackupReserve { get; init; }

    [JsonPropertyName("readback_backup_reserve")]
    public double ReadbackBackupReserve { get; init; }

    [JsonPropertyName("backup_reserve_enabled")]
    public bool BackupReserveEnabled { get; init; }

    [JsonPropertyName("applied")]
    public bool Applied { get; init; }

    [JsonPropertyName("idempotent")]
    public bool Idempotent { get; init; }
}
