using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace EcoFlow.EnergyManager;

public sealed class OpenMeteoActualWeatherProvider : IActualWeatherProvider
{
    private readonly HttpClient _httpClient;
    private readonly IRuntimeSettingsProvider _settingsProvider;

    public OpenMeteoActualWeatherProvider(
        HttpClient httpClient,
        IRuntimeSettingsProvider settingsProvider)
    {
        _httpClient = httpClient;
        _settingsProvider = settingsProvider;
    }

    public OpenMeteoActualWeatherProvider(HttpClient httpClient, EnergyManagerOptions options)
        : this(httpClient, new FixedRuntimeSettingsProvider(options))
    {
    }

    public async Task<WeatherObservation> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        var options = _settingsProvider.Current;
        var latitude = options.Latitude.ToString(CultureInfo.InvariantCulture);
        var longitude = options.Longitude.ToString(CultureInfo.InvariantCulture);
        var query =
            $"?latitude={latitude}&longitude={longitude}" +
            "&current=temperature_2m" +
            $"&timezone={Uri.EscapeDataString(options.TimeZone)}";

        var response = await _httpClient.GetFromJsonAsync<CurrentWeatherResponse>(
            query,
            cancellationToken) ?? throw new InvalidOperationException(
                "Open-Meteo returned an empty current-weather response.");

        if (!DateTime.TryParse(
                response.Current.Time,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localTime))
        {
            throw new InvalidOperationException("Open-Meteo returned an invalid observation time.");
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified),
            timeZone);

        return new WeatherObservation
        {
            ObservedUtc = new DateTimeOffset(utc, TimeSpan.Zero),
            AirTemperatureCelsius = response.Current.Temperature,
            Source = "open_meteo_best_match_current",
        };
    }

    private sealed record CurrentWeatherResponse
    {
        [JsonPropertyName("current")]
        public required CurrentWeather Current { get; init; }
    }

    private sealed record CurrentWeather
    {
        [JsonPropertyName("time")]
        public required string Time { get; init; }

        [JsonPropertyName("temperature_2m")]
        public required double Temperature { get; init; }
    }
}
