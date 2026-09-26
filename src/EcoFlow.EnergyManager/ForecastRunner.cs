namespace EcoFlow.EnergyManager;

public sealed class ForecastRunner(
    IEcoFlowGateway gateway,
    IWeatherProvider weatherProvider,
    SolarCalculator solarCalculator,
    ForecastSelector forecastSelector,
    IWeatherDataStore weatherDataStore,
    IEnergyPolicy policy,
    DecisionAuditWriter auditWriter,
    IRuntimeSettingsProvider settingsProvider)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await gateway.GetStatusAsync(cancellationToken);
            var weatherForecasts = await weatherProvider.GetTomorrowForecastsAsync(cancellationToken);
            var modelForecasts = weatherForecasts.Select(item => new ModelSolarForecast
            {
                Model = item.Model,
                Forecast = solarCalculator.Calculate(item.Weather),
            }).ToArray();
            await weatherDataStore.SaveForecastRunAsync(modelForecasts, cancellationToken);

            var accuracy = await weatherDataStore.GetModelAccuracyAsync(cancellationToken);
            await weatherDataStore.SaveAccuracySnapshotAsync(accuracy, cancellationToken);
            var selection = forecastSelector.Select(weatherForecasts, accuracy);
            var forecast = solarCalculator.Calculate(selection.Weather);
            var decision = policy.Evaluate(status, forecast, DateTimeOffset.UtcNow);

            PrintStatus(status);
            PrintModels(modelForecasts, selection, accuracy);
            PrintForecast(forecast);
            PrintDecision(decision);

            await auditWriter.AppendAsync(status, forecast, decision, cancellationToken);
            Console.WriteLine($"Audit log:    {settingsProvider.Current.DecisionLogPath}");
            return decision.IsActionable ? 0 : 2;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"EcoFlow forecast failed: {error.Message}");
            return 1;
        }
    }

    private static void PrintModels(
        IReadOnlyList<ModelSolarForecast> forecasts,
        ForecastSelection selection,
        IReadOnlyList<WeatherModelAccuracy> accuracy)
    {
        Console.WriteLine();
        Console.WriteLine("Weather models:");
        foreach (var item in forecasts)
        {
            var modelAccuracy = accuracy.FirstOrDefault(candidate => candidate.Model == item.Model);
            var accuracyText = modelAccuracy is null
                ? "no accuracy history"
                : $"MAE {modelAccuracy.MeanAbsoluteErrorCelsius:0.00}°C / n={modelAccuracy.SampleCount}";
            Console.WriteLine(
                $"  {item.Model,-20} {item.Forecast.ExpectedGenerationKwh,5:0.00} kWh | {accuracyText}");
        }

        Console.WriteLine($"Selected source: {selection.Source}");
    }

    private static void PrintStatus(EcoFlowStatus status)
    {
        Console.WriteLine($"Device:       {status.Model ?? "unknown"}");
        Console.WriteLine($"BLE:          {(status.Authenticated ? "authenticated" : status.BridgeState)}");
        Console.WriteLine($"Battery:      {Format(status.BatteryLevel, "%")}");
        Console.WriteLine($"Input power:  {Format(status.InputPowerW, "W")}");
        Console.WriteLine($"Output power: {Format(status.OutputPowerW, "W")}");
        Console.WriteLine($"AC ports:     {FormatSwitch(status.AcPorts)}");
        Console.WriteLine($"12V port:     {FormatSwitch(status.Dc12VPort)}");
        Console.WriteLine($"Charge range: {Format(status.ChargeLimitMin, "%")} - {Format(status.ChargeLimitMax, "%")}");
        Console.WriteLine($"Sampled UTC:  {status.SampledUtc:O}");
    }

    private static void PrintForecast(SolarForecast forecast)
    {
        Console.WriteLine();
        Console.WriteLine($"Forecast day: {forecast.ForecastDate:yyyy-MM-dd}");
        Console.WriteLine($"Expected generation tomorrow: {forecast.ExpectedGenerationKwh:0.00} kWh");
        Console.WriteLine($"Panel GTI:    {forecast.TotalTiltedIrradiationKwhM2:0.00} kWh/m²");
        foreach (var hour in forecast.Hours.Where(hour => hour.EnergyKwh > 0))
        {
            Console.WriteLine(
                $"{hour.LocalTime:HH:mm} | GTI {hour.GlobalTiltedIrradianceWm2,4:0} W/m² | " +
                $"Air {hour.AirTemperatureCelsius,5:0.0}°C | Panel {hour.PanelTemperatureCelsius,5:0.0}°C | " +
                $"{hour.EnergyKwh:0.000} kWh");
        }
    }

    private static void PrintDecision(EnergyDecision decision)
    {
        Console.WriteLine();
        Console.WriteLine("Mode:         DRY-RUN (no device commands)");
        Console.WriteLine($"Actionable:   {decision.IsActionable}");
        Console.WriteLine(
            $"Recommendation: {(decision.RecommendedUpperChargeLimit is null ? "none" : $"upper charge limit {decision.RecommendedUpperChargeLimit}%")}");
        Console.WriteLine($"Reason:       {decision.Reason}");
    }

    private static string Format(double? value, string unit) =>
        value is null ? "unknown" : $"{value:0.##} {unit}";

    private static string FormatSwitch(bool? value) => value switch
    {
        true => "on",
        false => "off",
        null => "unknown",
    };
}
