using System.Globalization;
using System.Text.Json;

namespace EcoFlow.EnergyManager;

public sealed class OpenMeteoWeatherProvider(
    HttpClient httpClient,
    EnergyManagerOptions options,
    TimeProvider? timeProvider = null) : IWeatherProvider
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<ModelWeatherForecast>> GetTomorrowForecastsAsync(
        CancellationToken cancellationToken = default)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        var nowUtc = _timeProvider.GetUtcNow();
        var tomorrow = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime).AddDays(1);

        var query = BuildQuery(tomorrow);
        using var response = await httpClient.GetAsync(query, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var hourly = document.RootElement.GetProperty("hourly");
        var times = hourly.GetProperty("time").EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        if (times.Length == 0)
        {
            throw new InvalidOperationException("Open-Meteo returned no hourly timestamps.");
        }

        var forecasts = new List<ModelWeatherForecast>(options.WeatherModels.Length);
        foreach (var model in options.WeatherModels)
        {
            var temperatureName = $"temperature_2m_{model}";
            var irradianceName = $"global_tilted_irradiance_{model}";
            if (!hourly.TryGetProperty(temperatureName, out var temperatures) ||
                !hourly.TryGetProperty(irradianceName, out var irradiances))
            {
                continue;
            }

            var temperatureValues = temperatures.EnumerateArray().ToArray();
            var irradianceValues = irradiances.EnumerateArray().ToArray();
            if (temperatureValues.Length != times.Length || irradianceValues.Length != times.Length)
            {
                throw new InvalidOperationException($"Open-Meteo returned incomplete arrays for {model}.");
            }

            var samples = new List<HourlySolarWeather>(times.Length);
            for (var index = 0; index < times.Length; index++)
            {
                if (temperatureValues[index].ValueKind == JsonValueKind.Null ||
                    irradianceValues[index].ValueKind == JsonValueKind.Null ||
                    !DateTime.TryParse(
                        times[index],
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var localTime) ||
                    DateOnly.FromDateTime(localTime) != tomorrow)
                {
                    continue;
                }

                samples.Add(new HourlySolarWeather
                {
                    LocalTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified),
                    GlobalTiltedIrradianceWm2 = irradianceValues[index].GetDouble(),
                    AirTemperatureCelsius = temperatureValues[index].GetDouble(),
                });
            }

            if (samples.Count > 0)
            {
                forecasts.Add(new ModelWeatherForecast
                {
                    Model = model,
                    Weather = new SolarWeatherForecast
                    {
                        ForecastDate = tomorrow,
                        FetchedUtc = nowUtc,
                        Hours = samples,
                    },
                });
            }
        }

        if (forecasts.Count == 0)
        {
            throw new InvalidOperationException("Open-Meteo returned no usable model forecasts.");
        }

        return forecasts;
    }

    private string BuildQuery(DateOnly tomorrow)
    {
        var latitude = options.Latitude.ToString(CultureInfo.InvariantCulture);
        var longitude = options.Longitude.ToString(CultureInfo.InvariantCulture);
        var date = tomorrow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var tilt = options.PanelTiltDegrees.ToString(CultureInfo.InvariantCulture);
        var azimuth = options.PanelAzimuthDegrees.ToString(CultureInfo.InvariantCulture);
        var models = Uri.EscapeDataString(string.Join(',', options.WeatherModels));
        return
            $"?latitude={latitude}&longitude={longitude}" +
            "&hourly=temperature_2m,global_tilted_irradiance" +
            $"&tilt={tilt}&azimuth={azimuth}" +
            $"&start_date={date}&end_date={date}" +
            $"&timezone={Uri.EscapeDataString(options.TimeZone)}" +
            $"&models={models}";
    }
}
