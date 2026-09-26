using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EcoFlow.EnergyManager;

public sealed class PvActualWorker(
    PvActualCollector collector,
    TimeProvider timeProvider,
    ILogger<PvActualWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var exitCode = await collector.RunAsync(stoppingToken);
            if (exitCode != 0)
            {
                logger.LogWarning("Actual PV collection completed with exit code {ExitCode}", exitCode);
            }

            await Task.Delay(TimeSpan.FromMinutes(1), timeProvider, stoppingToken);
        }
    }
}
