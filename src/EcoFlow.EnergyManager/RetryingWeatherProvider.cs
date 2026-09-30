using Microsoft.Extensions.Logging;

namespace EcoFlow.EnergyManager;

public sealed class RetryingWeatherProvider : IWeatherProvider
{
    private readonly IWeatherProvider _inner;
    private readonly IRuntimeSettingsProvider _settingsProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RetryingWeatherProvider> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public RetryingWeatherProvider(
        IWeatherProvider inner,
        IRuntimeSettingsProvider settingsProvider,
        TimeProvider timeProvider,
        ILogger<RetryingWeatherProvider> logger,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _inner = inner;
        _settingsProvider = settingsProvider;
        _timeProvider = timeProvider;
        _logger = logger;
        _delayAsync = delayAsync ?? ((delay, cancellationToken) =>
            Task.Delay(delay, timeProvider, cancellationToken));
    }

    public async Task<IReadOnlyList<ModelWeatherForecast>> GetTomorrowForecastsAsync(
        CancellationToken cancellationToken = default)
    {
        var options = _settingsProvider.Current;
        var startedUtc = _timeProvider.GetUtcNow();
        var deadlineUtc = startedUtc + options.WeatherRetryWindow;
        var attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                return await _inner.GetTomorrowForecastsAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                var remaining = deadlineUtc - _timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    _logger.LogError(
                        error,
                        "Weather forecast failed after {AttemptCount} attempts over {RetryWindow}",
                        attempt,
                        options.WeatherRetryWindow);
                    throw;
                }

                var delay = remaining < options.WeatherRetryInterval
                    ? remaining
                    : options.WeatherRetryInterval;
                _logger.LogWarning(
                    error,
                    "Weather forecast attempt {Attempt} failed; retrying in {Delay}",
                    attempt,
                    delay);
                await _delayAsync(delay, cancellationToken);
            }
        }
    }
}
