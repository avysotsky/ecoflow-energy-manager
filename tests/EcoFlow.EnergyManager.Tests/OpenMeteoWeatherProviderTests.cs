using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace EcoFlow.EnergyManager.Tests;

public sealed class OpenMeteoWeatherProviderTests
{
    [Fact]
    public async Task GetTomorrowForecastsAsync_ReadsEveryConfiguredModel()
    {
        var models = new[] { "ecmwf_ifs025", "icon_seamless", "gfs_seamless" };
        var hourly = new Dictionary<string, object>
        {
            ["time"] = Enumerable.Range(0, 24)
                .Select(hour => $"2026-09-27T{hour:00}:00")
                .ToArray(),
        };
        foreach (var model in models)
        {
            hourly[$"temperature_2m_{model}"] = Enumerable.Repeat(15d, 24).ToArray();
            hourly[$"global_tilted_irradiance_{model}"] = Enumerable.Repeat(100d, 24).ToArray();
        }

        var handler = new StubHandler(JsonSerializer.Serialize(new { hourly }));
        var provider = new OpenMeteoWeatherProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.test/forecast") },
            new EnergyManagerOptions { WeatherModels = models },
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero)));

        var result = await provider.GetTomorrowForecastsAsync();

        Assert.Equal(3, result.Count);
        Assert.All(result, item => Assert.Equal(24, item.Weather.Hours.Count));
        Assert.Contains("models=ecmwf_ifs025%2Cicon_seamless%2Cgfs_seamless", handler.RequestUri?.Query);
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
