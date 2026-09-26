using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EcoFlow.EnergyManager;

public sealed class ForecastWorker(
    ForecastRunner runner,
    EnergyManagerOptions options,
    TimeProvider timeProvider,
    ILogger<ForecastWorker> logger) : BackgroundService
{
    public ForecastWorker(
        ForecastRunner runner,
        EnergyManagerOptions options,
        ILogger<ForecastWorker> logger)
        : this(runner, options, TimeProvider.System, logger)
    {
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun(timeProvider.GetUtcNow());
            logger.LogInformation(
                "Next solar forecast calculation in {Delay}",
                delay);

            await Task.Delay(delay, timeProvider, stoppingToken);
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
