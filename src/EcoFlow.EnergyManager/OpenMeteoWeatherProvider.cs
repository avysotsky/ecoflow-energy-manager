using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace EcoFlow.EnergyManager;

public sealed class OpenMeteoWeatherProvider(
    HttpClient httpClient,
    EnergyManagerOptions options,
    TimeProvider? timeProvider = null) : IWeatherProvider
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<SolarWeatherForecast> GetTomorrowForecastAsync(
        CancellationToken cancellationToken = default)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        var nowUtc = _timeProvider.GetUtcNow();
        var tomorrow = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime).AddDays(1);

        var latitude = options.Latitude.ToString(CultureInfo.InvariantCulture);
        var longitude = options.Longitude.ToString(CultureInfo.InvariantCulture);
        var date = tomorrow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var tilt = options.PanelTiltDegrees.ToString(CultureInfo.InvariantCulture);
        var azimuth = options.PanelAzimuthDegrees.ToString(CultureInfo.InvariantCulture);
        var query =
            $"?latitude={latitude}&longitude={longitude}" +
            "&hourly=temperature_2m,global_tilted_irradiance" +
            $"&tilt={tilt}&azimuth={azimuth}" +
            $"&start_date={date}&end_date={date}" +
            $"&timezone={Uri.EscapeDataString(options.TimeZone)}";

        var response = await httpClient.GetFromJsonAsync<OpenMeteoResponse>(
            query,
            cancellationToken) ?? throw new InvalidOperationException(
                "Open-Meteo returned an empty response.");

        response.Hourly.Validate();

        var samples = new List<HourlySolarWeather>(response.Hourly.Time.Count);

        for (var index = 0; index < response.Hourly.Time.Count; index++)
        {
            if (!DateTime.TryParse(
                    response.Hourly.Time[index],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var sampleTime) ||
                DateOnly.FromDateTime(sampleTime) != tomorrow)
            {
                continue;
            }

            samples.Add(new HourlySolarWeather
            {
                LocalTime = sampleTime,
                GlobalTiltedIrradianceWm2 = response.Hourly.GlobalTiltedIrradiance[index],
                AirTemperatureCelsius = response.Hourly.Temperature[index],
            });
        }

        if (samples.Count < 20)
        {
            throw new InvalidOperationException(
                $"Open-Meteo returned only {samples.Count} hourly samples for {tomorrow:yyyy-MM-dd}.");
        }

        return new SolarWeatherForecast
        {
            ForecastDate = tomorrow,
            FetchedUtc = nowUtc,
            Hours = samples,
        };
    }

    private sealed record OpenMeteoResponse
    {
        [JsonPropertyName("hourly")]
        public required HourlyForecast Hourly { get; init; }
    }

    private sealed record HourlyForecast
    {
        [JsonPropertyName("time")]
        public required List<string> Time { get; init; }

        [JsonPropertyName("global_tilted_irradiance")]
        public required List<double> GlobalTiltedIrradiance { get; init; }

        [JsonPropertyName("temperature_2m")]
        public required List<double> Temperature { get; init; }

        public void Validate()
        {
            if (Time.Count == 0 ||
                GlobalTiltedIrradiance.Count != Time.Count ||
                Temperature.Count != Time.Count)
            {
                throw new InvalidOperationException(
                    "Open-Meteo returned incomplete hourly arrays.");
            }
        }
    }
}
