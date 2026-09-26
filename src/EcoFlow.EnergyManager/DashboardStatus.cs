namespace EcoFlow.EnergyManager;

public sealed record DashboardStatus
{
    public required string BridgeState { get; init; }
    public bool Connected { get; init; }
    public bool Authenticated { get; init; }
    public double? BatteryLevel { get; init; }
    public double? InputPowerW { get; init; }
    public double? OutputPowerW { get; init; }
    public double? NetPowerW { get; init; }
    public bool? AcPorts { get; init; }
    public bool? Dc12VPort { get; init; }
    public double? ChargeLimitMin { get; init; }
    public double? ChargeLimitMax { get; init; }
    public DateTimeOffset? SampledUtc { get; init; }
    public bool Stale { get; init; }
    public bool Error { get; init; }
    public string? ErrorMessage { get; init; }

    public static DashboardStatus FromStatus(EcoFlowStatus status, bool stale) => new()
    {
        BridgeState = SanitizeBridgeState(status.BridgeState, status.Connected),
        Connected = status.Connected,
        Authenticated = status.Authenticated,
        BatteryLevel = status.BatteryLevel,
        InputPowerW = status.InputPowerW,
        OutputPowerW = status.OutputPowerW,
        NetPowerW = status.InputPowerW is not null && status.OutputPowerW is not null
            ? status.InputPowerW - status.OutputPowerW
            : null,
        AcPorts = status.AcPorts,
        Dc12VPort = status.Dc12VPort,
        ChargeLimitMin = status.ChargeLimitMin,
        ChargeLimitMax = status.ChargeLimitMax,
        SampledUtc = status.SampledUtc == default ? null : status.SampledUtc,
        Stale = stale,
        Error = !string.IsNullOrWhiteSpace(status.LastError),
        ErrorMessage = string.IsNullOrWhiteSpace(status.LastError)
            ? null
            : "Мост сообщил об ошибке. Подробности доступны только в локальном журнале.",
    };

    public static DashboardStatus Unavailable() => new()
    {
        BridgeState = "недоступен",
        Stale = true,
        Error = true,
        ErrorMessage = "Не удалось получить состояние локального BLE-моста.",
    };

    private static string SanitizeBridgeState(string? state, bool connected)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return connected ? "подключён" : "не подключён";
        }

        var normalized = state.Trim().ToLowerInvariant();
        return normalized switch
        {
            "connected" or "ready" => "подключён",
            "authenticated" => "аутентифицирован",
            "connecting" or "discovering" => "подключение",
            "authenticating" => "аутентификация",
            "disconnected" or "idle" => "не подключён",
            "error" or "failed" => "ошибка",
            _ => connected ? "подключён" : "не подключён",
        };
    }
}
