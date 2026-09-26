namespace EcoFlow.EnergyManager;

public sealed class SolarCalculator(IRuntimeSettingsProvider settingsProvider)
{
    public SolarCalculator(EnergyManagerOptions options)
        : this(new FixedRuntimeSettingsProvider(options))
    {
    }

    public SolarForecast Calculate(SolarWeatherForecast weather)
    {
        var options = settingsProvider.Current;
        var hours = new List<HourlyGeneration>(weather.Hours.Count);
        var totalEnergyKwh = 0d;
        var totalTiltedIrradiationWhM2 = 0d;

        foreach (var sample in weather.Hours)
        {
            var irradiance = Math.Max(0, sample.GlobalTiltedIrradianceWm2);
            totalTiltedIrradiationWhM2 += irradiance;

            var panelTemperature = sample.AirTemperatureCelsius;
            var energyKwh = 0d;

            if (irradiance > 0)
            {
                panelTemperature += irradiance * 0.03;
                var temperatureFactor = 1 +
                    options.TemperatureCoefficientPerCelsius *
                    (panelTemperature - 25);
                temperatureFactor = Math.Clamp(temperatureFactor, 0.5, 1.1);

                energyKwh = options.NominalPowerKw *
                    (irradiance / 1000d) *
                    temperatureFactor *
                    options.SystemEfficiency;
                totalEnergyKwh += energyKwh;
            }

            hours.Add(new HourlyGeneration
            {
                LocalTime = sample.LocalTime,
                GlobalTiltedIrradianceWm2 = irradiance,
                AirTemperatureCelsius = sample.AirTemperatureCelsius,
                PanelTemperatureCelsius = panelTemperature,
                EnergyKwh = energyKwh,
            });
        }

        return new SolarForecast
        {
            ForecastDate = weather.ForecastDate,
            FetchedUtc = weather.FetchedUtc,
            ExpectedGenerationKwh = totalEnergyKwh,
            TotalTiltedIrradiationKwhM2 = totalTiltedIrradiationWhM2 / 1000d,
            HourlySamples = hours.Count,
            Hours = hours,
        };
    }
}
