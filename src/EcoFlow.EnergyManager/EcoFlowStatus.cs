using System.Text.Json.Serialization;

namespace EcoFlow.EnergyManager;

public sealed record EcoFlowStatus
{
    [JsonPropertyName("bridge_state")]
    public string? BridgeState { get; init; }

    [JsonPropertyName("connected")]
    public bool Connected { get; init; }

    [JsonPropertyName("authenticated")]
    public bool Authenticated { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("battery_level")]
    public double? BatteryLevel { get; init; }

    [JsonPropertyName("input_power_w")]
    public double? InputPowerW { get; init; }

    [JsonPropertyName("output_power_w")]
    public double? OutputPowerW { get; init; }

    [JsonPropertyName("xt60_1_input_power_w")]
    public double? Xt60Input1PowerW { get; init; }

    [JsonPropertyName("xt60_2_input_power_w")]
    public double? Xt60Input2PowerW { get; init; }

    [JsonPropertyName("solar_input_power_w")]
    public double? SolarInputPowerW { get; init; }

    [JsonPropertyName("ac_ports")]
    public bool? AcPorts { get; init; }

    [JsonPropertyName("dc_12v_port")]
    public bool? Dc12VPort { get; init; }

    [JsonPropertyName("charge_limit_min")]
    public double? ChargeLimitMin { get; init; }

    [JsonPropertyName("charge_limit_max")]
    public double? ChargeLimitMax { get; init; }

    [JsonPropertyName("sampled_utc")]
    public DateTimeOffset SampledUtc { get; init; }

    [JsonPropertyName("last_error")]
    public string? LastError { get; init; }
}
