using Xunit;

namespace EcoFlow.EnergyManager.Tests;

public sealed class ForecastSelectorTests
{
    [Fact]
    public void Select_UsesLowestMaeModelAfterMinimumHistory()
    {
        var selector = new ForecastSelector(new EnergyManagerOptions
        {
            MinimumAccuracySamples = 24,
        });
        var forecasts = new[]
        {
            CreateForecast("ecmwf_ifs025", 10),
            CreateForecast("icon_seamless", 20),
        };
        var accuracy = new[]
        {
            CreateAccuracy("ecmwf_ifs025", 48, 1.2),
            CreateAccuracy("icon_seamless", 48, 0.7),
        };

        var result = selector.Select(forecasts, accuracy);

        Assert.Equal("icon_seamless", result.Source);
        Assert.Equal(20, result.Weather.Hours[0].AirTemperatureCelsius);
        Assert.NotNull(result.Accuracy);
    }

    [Fact]
    public void Select_UsesEqualEnsembleUntilEnoughActualSamplesExist()
    {
        var selector = new ForecastSelector(new EnergyManagerOptions
        {
            MinimumAccuracySamples = 24,
        });
        var forecasts = new[]
        {
            CreateForecast("ecmwf_ifs025", 10),
            CreateForecast("icon_seamless", 20),
        };
        var accuracy = new[]
        {
            CreateAccuracy("icon_seamless", 23, 0.1),
        };

        var result = selector.Select(forecasts, accuracy);

        Assert.Equal("equal_model_ensemble", result.Source);
        Assert.Equal(15, result.Weather.Hours[0].AirTemperatureCelsius);
        Assert.Null(result.Accuracy);
    }

    private static ModelWeatherForecast CreateForecast(string model, double temperature) => new()
    {
        Model = model,
        Weather = new SolarWeatherForecast
        {
            ForecastDate = new DateOnly(2026, 9, 27),
            FetchedUtc = DateTimeOffset.UtcNow,
            Hours = Enumerable.Range(0, 24).Select(hour => new HourlySolarWeather
            {
                LocalTime = new DateTime(2026, 9, 27, hour, 0, 0),
                AirTemperatureCelsius = temperature,
                GlobalTiltedIrradianceWm2 = 100,
            }).ToArray(),
        },
    };

    private static WeatherModelAccuracy CreateAccuracy(
        string model,
        int sampleCount,
        double mae) => new()
    {
        Model = model,
        SampleCount = sampleCount,
        MeanAbsoluteErrorCelsius = mae,
        RootMeanSquareErrorCelsius = mae * 1.2,
    };
}
