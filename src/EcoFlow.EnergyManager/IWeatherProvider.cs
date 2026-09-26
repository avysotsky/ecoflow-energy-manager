namespace EcoFlow.EnergyManager;

public interface IWeatherProvider
{
    Task<IReadOnlyList<ModelWeatherForecast>> GetTomorrowForecastsAsync(
        CancellationToken cancellationToken = default);
}
