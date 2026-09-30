using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EcoFlow.EnergyManager.Tests;

public sealed class RetryingWeatherProviderTests
{
    [Fact]
    public async Task GetTomorrowForecastsAsync_RetriesEveryMinuteUntilSuccess()
    {
        var timeProvider = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 9, 30, 20, 0, 0, TimeSpan.Zero));
        var inner = new StubWeatherProvider(failuresBeforeSuccess: 3);
        var delays = new List<TimeSpan>();
        var retrying = CreateProvider(inner, timeProvider, delays);

        var result = await retrying.GetTomorrowForecastsAsync();

        Assert.Single(result);
        Assert.Equal(4, inner.AttemptCount);
        Assert.Equal(3, delays.Count);
        Assert.All(delays, delay => Assert.Equal(TimeSpan.FromMinutes(1), delay));
    }

    [Fact]
    public async Task GetTomorrowForecastsAsync_StopsAfterOneHour()
    {
        var timeProvider = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 9, 30, 20, 0, 0, TimeSpan.Zero));
        var inner = new StubWeatherProvider(failuresBeforeSuccess: int.MaxValue);
        var delays = new List<TimeSpan>();
        var retrying = CreateProvider(inner, timeProvider, delays);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => retrying.GetTomorrowForecastsAsync());

        Assert.Equal(61, inner.AttemptCount);
        Assert.Equal(60, delays.Count);
        Assert.All(delays, delay => Assert.Equal(TimeSpan.FromMinutes(1), delay));
    }

    private static RetryingWeatherProvider CreateProvider(
        IWeatherProvider inner,
        AdjustableTimeProvider timeProvider,
        List<TimeSpan> delays)
    {
        var options = new EnergyManagerOptions
        {
            WeatherRetryInterval = TimeSpan.FromMinutes(1),
            WeatherRetryWindow = TimeSpan.FromHours(1),
        };
        return new RetryingWeatherProvider(
            inner,
            new FixedRuntimeSettingsProvider(options),
            timeProvider,
            NullLogger<RetryingWeatherProvider>.Instance,
            (delay, _) =>
            {
                delays.Add(delay);
                timeProvider.Advance(delay);
                return Task.CompletedTask;
            });
    }

    private sealed class StubWeatherProvider(int failuresBeforeSuccess) : IWeatherProvider
    {
        public int AttemptCount { get; private set; }

        public Task<IReadOnlyList<ModelWeatherForecast>> GetTomorrowForecastsAsync(
            CancellationToken cancellationToken = default)
        {
            AttemptCount++;
            if (AttemptCount <= failuresBeforeSuccess)
            {
                throw new HttpRequestException("Temporary weather failure.");
            }

            IReadOnlyList<ModelWeatherForecast> result =
            [
                new ModelWeatherForecast
                {
                    Model = "test",
                    Weather = new SolarWeatherForecast
                    {
                        ForecastDate = new DateOnly(2026, 10, 1),
                        FetchedUtc = new DateTimeOffset(2026, 9, 30, 20, 0, 0, TimeSpan.Zero),
                        Hours = [],
                    },
                },
            ];
            return Task.FromResult(result);
        }
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
