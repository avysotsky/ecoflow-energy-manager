using EcoFlow.EnergyManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var options = EnergyManagerOptions.FromEnvironment();
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient("bridge", client =>
{
    client.BaseAddress = new Uri(
        Environment.GetEnvironmentVariable("ECOFLOW_BRIDGE_URL") ??
        "http://127.0.0.1:8765");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient("weather", client =>
{
    client.BaseAddress = new Uri(
        Environment.GetEnvironmentVariable("OPEN_METEO_URL") ??
        "https://api.open-meteo.com/v1/forecast");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<IEcoFlowGateway>(services =>
    new LocalBridgeEcoFlowGateway(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("bridge")));
builder.Services.AddSingleton<IWeatherProvider>(services =>
    new OpenMeteoWeatherProvider(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("weather"),
        options));
builder.Services.AddSingleton<IActualWeatherProvider>(services =>
    new OpenMeteoActualWeatherProvider(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("weather"),
        options));
builder.Services.AddSingleton<IWeatherDataStore, PostgresWeatherDataStore>();
builder.Services.AddSingleton<SolarCalculator>();
builder.Services.AddSingleton<ForecastSelector>();
builder.Services.AddSingleton<IEnergyPolicy, DryRunEnergyPolicy>();
builder.Services.AddSingleton(new DecisionAuditWriter(options.DecisionLogPath));
builder.Services.AddSingleton<ForecastRunner>();
builder.Services.AddSingleton<ActualWeatherCollector>();

var runOnce = args.Contains("--once", StringComparer.OrdinalIgnoreCase);
var collectActual = args.Contains("--collect-actual", StringComparer.OrdinalIgnoreCase);
if (!runOnce && !collectActual)
{
    builder.Services.AddHostedService<ForecastWorker>();
    builder.Services.AddHostedService<ActualWeatherWorker>();
}

using var host = builder.Build();

if (runOnce)
{
    var runner = host.Services.GetRequiredService<ForecastRunner>();
    return await runner.RunAsync(CancellationToken.None);
}

if (collectActual)
{
    var collector = host.Services.GetRequiredService<ActualWeatherCollector>();
    return await collector.RunAsync(CancellationToken.None);
}

await host.RunAsync();
return 0;
