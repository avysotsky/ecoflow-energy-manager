namespace EcoFlow.EnergyManager;

public sealed class ForecastSelector(IRuntimeSettingsProvider settingsProvider)
{
    public ForecastSelector(EnergyManagerOptions options)
        : this(new FixedRuntimeSettingsProvider(options))
    {
    }

    public ForecastSelection Select(
        IReadOnlyList<ModelWeatherForecast> forecasts,
        IReadOnlyList<WeatherModelAccuracy> accuracy)
    {
        if (forecasts.Count == 0)
        {
            throw new ArgumentException("At least one forecast is required.", nameof(forecasts));
        }

        var options = settingsProvider.Current;
        var eligible = accuracy
            .Where(item => item.SampleCount >= options.MinimumAccuracySamples)
            .OrderBy(item => item.MeanAbsoluteErrorCelsius)
            .ThenBy(item => item.RootMeanSquareErrorCelsius)
            .ToArray();

        foreach (var item in eligible)
        {
            var forecast = forecasts.FirstOrDefault(candidate =>
                string.Equals(candidate.Model, item.Model, StringComparison.OrdinalIgnoreCase) &&
                candidate.Weather.Hours.Count >= 20);
            if (forecast is not null)
            {
                return new ForecastSelection
                {
                    Source = forecast.Model,
                    Weather = forecast.Weather,
                    Accuracy = item,
                };
            }
        }

        return new ForecastSelection
        {
            Source = "equal_model_ensemble",
            Weather = Average(forecasts),
        };
    }

    private static SolarWeatherForecast Average(IReadOnlyList<ModelWeatherForecast> forecasts)
    {
        var complete = forecasts.Where(forecast => forecast.Weather.Hours.Count >= 20).ToArray();
        if (complete.Length == 0)
        {
            throw new InvalidOperationException("No weather model returned a complete daily forecast.");
        }

        var byTime = complete
            .SelectMany(forecast => forecast.Weather.Hours)
            .GroupBy(hour => hour.LocalTime)
            .OrderBy(group => group.Key)
            .Select(group => new HourlySolarWeather
            {
                LocalTime = group.Key,
                GlobalTiltedIrradianceWm2 = group.Average(hour => hour.GlobalTiltedIrradianceWm2),
                AirTemperatureCelsius = group.Average(hour => hour.AirTemperatureCelsius),
            })
            .ToArray();

        return new SolarWeatherForecast
        {
            ForecastDate = complete[0].Weather.ForecastDate,
            FetchedUtc = complete.Max(forecast => forecast.Weather.FetchedUtc),
            Hours = byTime,
        };
    }
}
