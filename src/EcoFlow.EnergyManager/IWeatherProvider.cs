namespace EcoFlow.EnergyManager;

public interface IWeatherProvider
{
    Task<SolarWeatherForecast> GetTomorrowForecastAsync(
        CancellationToken cancellationToken = default);
}
