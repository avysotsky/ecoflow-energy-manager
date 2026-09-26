using Xunit;

namespace EcoFlow.EnergyManager.Tests;

public sealed class SolarCalculatorTests
{
    [Fact]
    public void Calculate_AppliesPanelTemperatureAndSystemLosses()
    {
        var calculator = new SolarCalculator(new EnergyManagerOptions
        {
            NominalPowerKw = 1,
            SystemEfficiency = 0.85,
            TemperatureCoefficientPerCelsius = -0.004,
        });
        var weather = new SolarWeatherForecast
        {
            ForecastDate = new DateOnly(2026, 9, 27),
            FetchedUtc = DateTimeOffset.UtcNow,
            Hours =
            [
                new HourlySolarWeather
                {
                    LocalTime = new DateTime(2026, 9, 27, 12, 0, 0),
                    GlobalTiltedIrradianceWm2 = 700,
                    AirTemperatureCelsius = 25,
                },
            ],
        };

        var result = calculator.Calculate(weather);

        Assert.Equal(46, result.Hours[0].PanelTemperatureCelsius, 6);
        Assert.Equal(0.54502, result.ExpectedGenerationKwh, 5);
        Assert.Equal(0.7, result.TotalTiltedIrradiationKwhM2, 6);
    }

    [Fact]
    public void Calculate_ProducesZeroEnergyAtNight()
    {
        var calculator = new SolarCalculator(new EnergyManagerOptions());
        var weather = new SolarWeatherForecast
        {
            ForecastDate = new DateOnly(2026, 9, 27),
            FetchedUtc = DateTimeOffset.UtcNow,
            Hours =
            [
                new HourlySolarWeather
                {
                    LocalTime = new DateTime(2026, 9, 27, 1, 0, 0),
                    GlobalTiltedIrradianceWm2 = 0,
                    AirTemperatureCelsius = 15,
                },
            ],
        };

        var result = calculator.Calculate(weather);

        Assert.Equal(0, result.ExpectedGenerationKwh);
        Assert.Equal(15, result.Hours[0].PanelTemperatureCelsius);
    }
}
