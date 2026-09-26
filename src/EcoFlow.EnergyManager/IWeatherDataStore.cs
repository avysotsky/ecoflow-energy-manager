namespace EcoFlow.EnergyManager;

public interface IWeatherDataStore
{
    Task SaveForecastRunAsync(
        IReadOnlyList<ModelSolarForecast> forecasts,
        CancellationToken cancellationToken = default);

    Task SaveActualObservationAsync(
        WeatherObservation observation,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WeatherModelAccuracy>> GetModelAccuracyAsync(
        CancellationToken cancellationToken = default);

    Task SaveAccuracySnapshotAsync(
        IReadOnlyList<WeatherModelAccuracy> accuracy,
        CancellationToken cancellationToken = default);
}
