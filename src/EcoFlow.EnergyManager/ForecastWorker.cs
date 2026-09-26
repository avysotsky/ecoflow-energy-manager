using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EcoFlow.EnergyManager;

public sealed class ForecastWorker(
    ForecastRunner runner,
    IRuntimeSettingsProvider settingsProvider,
    TimeProvider timeProvider,
    ILogger<ForecastWorker> logger) : BackgroundService
{
    public ForecastWorker(
        ForecastRunner runner,
        EnergyManagerOptions options,
        ILogger<ForecastWorker> logger)
        : this(runner, new FixedRuntimeSettingsProvider(options), TimeProvider.System, logger)
    {
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun(timeProvider.GetUtcNow());
            var settingsChanged = settingsProvider.SettingsChanged;
            logger.LogInformation(
                "Next solar forecast calculation in {Delay}",
                delay);

            var scheduledDelay = Task.Delay(delay, timeProvider, stoppingToken);
            if (await Task.WhenAny(scheduledDelay, settingsChanged) == settingsChanged)
            {
                logger.LogInformation("Forecast schedule changed; recalculating next run time");
                continue;
            }

            await scheduledDelay;
            var exitCode = await runner.RunAsync(stoppingToken);
            if (exitCode != 0)
            {
                logger.LogWarning(
                    "Solar forecast calculation completed with exit code {ExitCode}",
                    exitCode);
            }
        }
    }

    internal TimeSpan GetDelayUntilNextRun(DateTimeOffset nowUtc)
    {
        var options = settingsProvider.Current;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var nextLocal = localNow.Date.AddHours(options.ForecastRunHourLocal);

        if (localNow.DateTime >= nextLocal)
        {
            nextLocal = nextLocal.AddDays(1);
        }

        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified),
            timeZone);
        return nextUtc - nowUtc.UtcDateTime;
    }
}
