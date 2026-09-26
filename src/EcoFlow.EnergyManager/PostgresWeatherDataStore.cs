using Npgsql;

namespace EcoFlow.EnergyManager;

public sealed class PostgresWeatherDataStore : IWeatherDataStore, IAsyncDisposable
{
    private readonly IRuntimeSettingsProvider _settingsProvider;
    private readonly NpgsqlDataSource _dataSource;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public PostgresWeatherDataStore(IRuntimeSettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider;
        var options = settingsProvider.Current;
        _dataSource = NpgsqlDataSource.Create(options.PostgresConnectionString);
    }

    public async Task SaveForecastRunAsync(
        IReadOnlyList<ModelSolarForecast> forecasts,
        CancellationToken cancellationToken = default)
    {
        if (forecasts.Count == 0)
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var first = forecasts[0].Forecast;
        await using var runCommand = new NpgsqlCommand(
            """
            INSERT INTO weather_forecast_runs
                (fetched_utc, forecast_date, latitude, longitude)
            VALUES
                (@fetched_utc, @forecast_date, @latitude, @longitude)
            RETURNING id;
            """,
            connection,
            transaction);
        runCommand.Parameters.AddWithValue("fetched_utc", first.FetchedUtc);
        runCommand.Parameters.AddWithValue("forecast_date", first.ForecastDate);
        var options = _settingsProvider.Current;
        runCommand.Parameters.AddWithValue("latitude", options.Latitude);
        runCommand.Parameters.AddWithValue("longitude", options.Longitude);
        var runId = (long)(await runCommand.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("PostgreSQL did not return a forecast run id."));

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        foreach (var modelForecast in forecasts)
        {
            var forecast = modelForecast.Forecast;
            await using var summaryCommand = new NpgsqlCommand(
                """
                INSERT INTO solar_generation_forecasts
                    (run_id, model, expected_generation_kwh, total_gti_kwh_m2, hourly_samples)
                VALUES
                    (@run_id, @model, @energy, @gti, @samples);
                """,
                connection,
                transaction);
            summaryCommand.Parameters.AddWithValue("run_id", runId);
            summaryCommand.Parameters.AddWithValue("model", modelForecast.Model);
            summaryCommand.Parameters.AddWithValue("energy", forecast.ExpectedGenerationKwh);
            summaryCommand.Parameters.AddWithValue("gti", forecast.TotalTiltedIrradiationKwhM2);
            summaryCommand.Parameters.AddWithValue("samples", forecast.HourlySamples);
            await summaryCommand.ExecuteNonQueryAsync(cancellationToken);

            foreach (var hour in forecast.Hours)
            {
                var forecastUtc = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(hour.LocalTime, DateTimeKind.Unspecified),
                    timeZone);
                await using var hourCommand = new NpgsqlCommand(
                    """
                    INSERT INTO weather_forecast_hourly
                        (run_id, model, forecast_time_utc, air_temperature_c,
                         global_tilted_irradiance_wm2, panel_temperature_c, expected_generation_kwh)
                    VALUES
                        (@run_id, @model, @forecast_time_utc, @air_temperature_c,
                         @gti, @panel_temperature_c, @energy);
                    """,
                    connection,
                    transaction);
                hourCommand.Parameters.AddWithValue("run_id", runId);
                hourCommand.Parameters.AddWithValue("model", modelForecast.Model);
                hourCommand.Parameters.AddWithValue(
                    "forecast_time_utc",
                    new DateTimeOffset(forecastUtc, TimeSpan.Zero));
                hourCommand.Parameters.AddWithValue("air_temperature_c", hour.AirTemperatureCelsius);
                hourCommand.Parameters.AddWithValue("gti", hour.GlobalTiltedIrradianceWm2);
                hourCommand.Parameters.AddWithValue("panel_temperature_c", hour.PanelTemperatureCelsius);
                hourCommand.Parameters.AddWithValue("energy", hour.EnergyKwh);
                await hourCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveActualObservationAsync(
        WeatherObservation observation,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var utc = observation.ObservedUtc.UtcDateTime;
        var observedHour = new DateTimeOffset(
            utc.Year,
            utc.Month,
            utc.Day,
            utc.Hour,
            0,
            0,
            TimeSpan.Zero);

        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO weather_actual_hourly
                (observed_hour_utc, source, sampled_utc, air_temperature_c)
            VALUES
                (@observed_hour_utc, @source, @sampled_utc, @air_temperature_c)
            ON CONFLICT (observed_hour_utc, source) DO UPDATE SET
                sampled_utc = EXCLUDED.sampled_utc,
                air_temperature_c = EXCLUDED.air_temperature_c;
            """);
        command.Parameters.AddWithValue("observed_hour_utc", observedHour);
        command.Parameters.AddWithValue("source", observation.Source);
        command.Parameters.AddWithValue("sampled_utc", observation.ObservedUtc);
        command.Parameters.AddWithValue("air_temperature_c", observation.AirTemperatureCelsius);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WeatherModelAccuracy>> GetModelAccuracyAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var windowStart = DateTimeOffset.UtcNow.AddDays(-_settingsProvider.Current.AccuracyWindowDays);
        await using var command = _dataSource.CreateCommand(
            """
            WITH ranked AS
            (
                SELECT
                    hourly.model,
                    hourly.forecast_time_utc,
                    hourly.air_temperature_c,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY hourly.model, hourly.forecast_time_utc
                        ORDER BY runs.fetched_utc DESC
                    ) AS row_number
                FROM weather_forecast_hourly AS hourly
                JOIN weather_forecast_runs AS runs ON runs.id = hourly.run_id
                WHERE hourly.forecast_time_utc >= @window_start
                  AND runs.fetched_utc <= hourly.forecast_time_utc
            ),
            latest AS
            (
                SELECT model, forecast_time_utc, air_temperature_c
                FROM ranked
                WHERE row_number = 1
            )
            SELECT
                latest.model,
                COUNT(*)::integer,
                AVG(ABS(latest.air_temperature_c - actual.air_temperature_c))::double precision,
                SQRT(AVG(POWER(latest.air_temperature_c - actual.air_temperature_c, 2)))::double precision
            FROM latest
            JOIN weather_actual_hourly AS actual
              ON actual.observed_hour_utc = latest.forecast_time_utc
             AND actual.source = 'open_meteo_best_match_current'
            GROUP BY latest.model
            ORDER BY AVG(ABS(latest.air_temperature_c - actual.air_temperature_c));
            """);
        command.Parameters.AddWithValue("window_start", windowStart);

        var results = new List<WeatherModelAccuracy>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new WeatherModelAccuracy
            {
                Model = reader.GetString(0),
                SampleCount = reader.GetInt32(1),
                MeanAbsoluteErrorCelsius = reader.GetDouble(2),
                RootMeanSquareErrorCelsius = reader.GetDouble(3),
            });
        }

        return results;
    }

    public async Task SaveAccuracySnapshotAsync(
        IReadOnlyList<WeatherModelAccuracy> accuracy,
        CancellationToken cancellationToken = default)
    {
        if (accuracy.Count == 0)
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken);
        var calculatedUtc = DateTimeOffset.UtcNow;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        for (var rank = 0; rank < accuracy.Count; rank++)
        {
            var item = accuracy[rank];
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO weather_model_accuracy_snapshots
                    (calculated_utc, model, sample_count, mae_c, rmse_c, rank)
                VALUES
                    (@calculated_utc, @model, @sample_count, @mae_c, @rmse_c, @rank);
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("calculated_utc", calculatedUtc);
            command.Parameters.AddWithValue("model", item.Model);
            command.Parameters.AddWithValue("sample_count", item.SampleCount);
            command.Parameters.AddWithValue("mae_c", item.MeanAbsoluteErrorCelsius);
            command.Parameters.AddWithValue("rmse_c", item.RootMeanSquareErrorCelsius);
            command.Parameters.AddWithValue("rank", rank + 1);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        _initializationLock.Dispose();
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var command = _dataSource.CreateCommand(
                """
                CREATE TABLE IF NOT EXISTS weather_forecast_runs
                (
                    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    fetched_utc timestamptz NOT NULL,
                    forecast_date date NOT NULL,
                    latitude double precision NOT NULL,
                    longitude double precision NOT NULL,
                    created_utc timestamptz NOT NULL DEFAULT now()
                );

                CREATE TABLE IF NOT EXISTS weather_forecast_hourly
                (
                    run_id bigint NOT NULL REFERENCES weather_forecast_runs(id) ON DELETE CASCADE,
                    model text NOT NULL,
                    forecast_time_utc timestamptz NOT NULL,
                    air_temperature_c double precision NOT NULL,
                    global_tilted_irradiance_wm2 double precision NOT NULL,
                    panel_temperature_c double precision NOT NULL,
                    expected_generation_kwh double precision NOT NULL,
                    PRIMARY KEY (run_id, model, forecast_time_utc)
                );

                CREATE INDEX IF NOT EXISTS ix_weather_forecast_hourly_model_time
                    ON weather_forecast_hourly (model, forecast_time_utc);

                CREATE TABLE IF NOT EXISTS solar_generation_forecasts
                (
                    run_id bigint NOT NULL REFERENCES weather_forecast_runs(id) ON DELETE CASCADE,
                    model text NOT NULL,
                    expected_generation_kwh double precision NOT NULL,
                    total_gti_kwh_m2 double precision NOT NULL,
                    hourly_samples integer NOT NULL,
                    PRIMARY KEY (run_id, model)
                );

                CREATE TABLE IF NOT EXISTS weather_actual_hourly
                (
                    observed_hour_utc timestamptz NOT NULL,
                    source text NOT NULL,
                    sampled_utc timestamptz NOT NULL,
                    air_temperature_c double precision NOT NULL,
                    PRIMARY KEY (observed_hour_utc, source)
                );

                CREATE TABLE IF NOT EXISTS weather_model_accuracy_snapshots
                (
                    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    calculated_utc timestamptz NOT NULL,
                    model text NOT NULL,
                    sample_count integer NOT NULL,
                    mae_c double precision NOT NULL,
                    rmse_c double precision NOT NULL,
                    rank integer NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_weather_model_accuracy_calculated
                    ON weather_model_accuracy_snapshots (calculated_utc DESC, rank);
                """);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }
}
