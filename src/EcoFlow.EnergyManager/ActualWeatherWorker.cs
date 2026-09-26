using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EcoFlow.EnergyManager;

public sealed class ActualWeatherWorker(
    ActualWeatherCollector collector,
    TimeProvider timeProvider,
    ILogger<ActualWeatherWorker> logger) : BackgroundService
{
    public ActualWeatherWorker(
        ActualWeatherCollector collector,
        ILogger<ActualWeatherWorker> logger)
        : this(collector, TimeProvider.System, logger)
    {
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CollectAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun(timeProvider.GetUtcNow());
            logger.LogInformation("Next actual weather collection in {Delay}", delay);
            await Task.Delay(delay, timeProvider, stoppingToken);
            await CollectAsync(stoppingToken);
        }
    }

    internal static TimeSpan GetDelayUntilNextRun(DateTimeOffset nowUtc)
    {
        var next = new DateTimeOffset(
            nowUtc.Year,
            nowUtc.Month,
            nowUtc.Day,
            nowUtc.Hour,
            5,
            0,
            TimeSpan.Zero);
        if (nowUtc >= next)
        {
            next = next.AddHours(1);
        }

        return next - nowUtc;
    }

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        var exitCode = await collector.RunAsync(cancellationToken);
        if (exitCode != 0)
        {
            logger.LogWarning(
                "Actual weather collection completed with exit code {ExitCode}",
                exitCode);
        }
    }
}
