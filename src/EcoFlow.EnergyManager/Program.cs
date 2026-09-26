using EcoFlow.EnergyManager;

var bridgeUrl = Environment.GetEnvironmentVariable("ECOFLOW_BRIDGE_URL")
    ?? "http://127.0.0.1:8765";

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(bridgeUrl),
    Timeout = TimeSpan.FromSeconds(10),
};

IEcoFlowGateway gateway = new LocalBridgeEcoFlowGateway(httpClient);

try
{
    var status = await gateway.GetStatusAsync();
    Console.WriteLine($"Device:       {status.Model ?? "unknown"}");
    Console.WriteLine($"BLE:          {(status.Authenticated ? "authenticated" : status.BridgeState)}");
    Console.WriteLine($"Battery:      {Format(status.BatteryLevel, "%")}");
    Console.WriteLine($"Input power:  {Format(status.InputPowerW, "W")}");
    Console.WriteLine($"Output power: {Format(status.OutputPowerW, "W")}");
    Console.WriteLine($"AC ports:     {FormatSwitch(status.AcPorts)}");
    Console.WriteLine($"12V port:     {FormatSwitch(status.Dc12VPort)}");
    Console.WriteLine($"Charge range: {Format(status.ChargeLimitMin, "%")} - {Format(status.ChargeLimitMax, "%")}");
    Console.WriteLine($"Sampled UTC:  {status.SampledUtc:O}");

    if (!status.Authenticated)
    {
        Console.Error.WriteLine($"Bridge error: {status.LastError ?? "not connected"}");
        return 2;
    }

    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"EcoFlow status failed: {error.Message}");
    return 1;
}

static string Format(double? value, string unit) =>
    value is null ? "unknown" : $"{value:0.##} {unit}";

static string FormatSwitch(bool? value) => value switch
{
    true => "on",
    false => "off",
    null => "unknown",
};
