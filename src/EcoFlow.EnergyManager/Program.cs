using EcoFlow.EnergyManager;

var defaults = EnergyManagerOptions.FromEnvironment();
var settingsPath = Environment.GetEnvironmentVariable("ECOFLOW_SETTINGS_PATH");
if (string.IsNullOrWhiteSpace(settingsPath))
{
    settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ecoflow-energy-manager",
        "settings.json");
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.WebHost.UseUrls(
    Environment.GetEnvironmentVariable("ECOFLOW_WEB_URLS") ?? "http://127.0.0.1:5095");

builder.Services.AddSingleton<IRuntimeSettingsProvider>(
    new RuntimeSettingsProvider(defaults, settingsPath));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient("bridge", client =>
{
    client.BaseAddress = new Uri(
        Environment.GetEnvironmentVariable("ECOFLOW_BRIDGE_URL") ??
        "http://127.0.0.1:8765");
    client.Timeout = TimeSpan.FromSeconds(15);
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
builder.Services.AddSingleton<OpenMeteoWeatherProvider>(services =>
    new OpenMeteoWeatherProvider(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("weather"),
        services.GetRequiredService<IRuntimeSettingsProvider>()));
builder.Services.AddSingleton<IWeatherProvider>(services =>
    new RetryingWeatherProvider(
        services.GetRequiredService<OpenMeteoWeatherProvider>(),
        services.GetRequiredService<IRuntimeSettingsProvider>(),
        services.GetRequiredService<TimeProvider>(),
        services.GetRequiredService<ILogger<RetryingWeatherProvider>>()));
builder.Services.AddSingleton<IActualWeatherProvider>(services =>
    new OpenMeteoActualWeatherProvider(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("weather"),
        services.GetRequiredService<IRuntimeSettingsProvider>()));
builder.Services.AddSingleton<PostgresWeatherDataStore>();
builder.Services.AddSingleton<IWeatherDataStore>(services =>
    services.GetRequiredService<PostgresWeatherDataStore>());
builder.Services.AddSingleton<IPvDataStore>(services =>
    services.GetRequiredService<PostgresWeatherDataStore>());
builder.Services.AddSingleton<SolarCalculator>();
builder.Services.AddSingleton<ForecastSelector>();
builder.Services.AddSingleton<IEnergyPolicy, DryRunEnergyPolicy>();
builder.Services.AddSingleton<IBackupReserveControlStateStore>(services =>
    new FileBackupReserveControlStateStore(
        services.GetRequiredService<IRuntimeSettingsProvider>().Current.ControlStatePath));
builder.Services.AddSingleton<BackupReserveController>();
builder.Services.AddSingleton(new DecisionAuditWriter(defaults.DecisionLogPath));
builder.Services.AddSingleton<IForecastNotifier>(new TelegramCommandForecastNotifier(
    defaults.TelegramNotificationCommand,
    defaults.TelegramNotificationTimeout));
builder.Services.AddSingleton<ForecastRunner>();
builder.Services.AddSingleton<ActualWeatherCollector>();
builder.Services.AddSingleton<PvActualCollector>();

var runOnce = args.Contains("--once", StringComparer.OrdinalIgnoreCase);
var collectActual = args.Contains("--collect-actual", StringComparer.OrdinalIgnoreCase);
if (!runOnce && !collectActual)
{
    builder.Services.AddHostedService<ForecastWorker>();
    builder.Services.AddHostedService<ActualWeatherWorker>();
    builder.Services.AddHostedService<PvActualWorker>();
}

await using var app = builder.Build();

if (runOnce)
{
    var runner = app.Services.GetRequiredService<ForecastRunner>();
    return await runner.RunAsync(CancellationToken.None);
}

if (collectActual)
{
    var collector = app.Services.GetRequiredService<ActualWeatherCollector>();
    return await collector.RunAsync(CancellationToken.None);
}

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl = "no-cache";
    },
});

app.MapGet("/api/status", async (
    IEcoFlowGateway gateway,
    IRuntimeSettingsProvider settingsProvider,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    try
    {
        var status = await gateway.GetStatusAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var age = now - status.SampledUtc;
        var stale = status.SampledUtc == default ||
            age < TimeSpan.Zero ||
            age > settingsProvider.Current.MaximumStatusAge;
        return Results.Ok(DashboardStatus.FromStatus(status, stale));
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch
    {
        return Results.Ok(DashboardStatus.Unavailable());
    }
});

app.MapGet("/api/settings", (IRuntimeSettingsProvider settingsProvider) =>
    Results.Ok(settingsProvider.PublicSettings));

app.MapPut("/api/settings", async (
    RuntimeSettings settings,
    IRuntimeSettingsProvider settingsProvider,
    CancellationToken cancellationToken) =>
{
    var errors = await settingsProvider.UpdateAsync(settings, cancellationToken);
    return errors.Count == 0
        ? Results.Ok(settingsProvider.PublicSettings)
        : Results.ValidationProblem(errors.ToDictionary(item => item.Key, item => item.Value));
});

await app.RunAsync();
return 0;

public partial class Program;
