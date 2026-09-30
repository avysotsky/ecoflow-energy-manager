namespace EcoFlow.EnergyManager;

public sealed class ForecastRunner(
    IEcoFlowGateway gateway,
    IWeatherProvider weatherProvider,
    SolarCalculator solarCalculator,
    ForecastSelector forecastSelector,
    IWeatherDataStore weatherDataStore,
    IEnergyPolicy policy,
    BackupReserveController backupReserveController,
    DecisionAuditWriter auditWriter,
    IRuntimeSettingsProvider settingsProvider,
    IForecastNotifier notifier)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        EcoFlowStatus? status = null;
        ForecastSelection? selection = null;
        SolarForecast? forecast = null;
        EnergyDecision? decision = null;
        BackupReserveControlOutcome? control = null;
        var exitCode = 1;
        string? failure = null;
        try
        {
            status = await gateway.GetStatusAsync(cancellationToken);
            var weatherForecasts = await weatherProvider.GetTomorrowForecastsAsync(cancellationToken);
            var modelForecasts = weatherForecasts.Select(item => new ModelSolarForecast
            {
                Model = item.Model,
                Forecast = solarCalculator.Calculate(item.Weather),
            }).ToArray();
            await weatherDataStore.SaveForecastRunAsync(modelForecasts, cancellationToken);

            var accuracy = await weatherDataStore.GetModelAccuracyAsync(cancellationToken);
            await weatherDataStore.SaveAccuracySnapshotAsync(accuracy, cancellationToken);
            selection = forecastSelector.Select(weatherForecasts, accuracy);
            forecast = solarCalculator.Calculate(selection.Weather);
            var nowUtc = DateTimeOffset.UtcNow;
            decision = policy.Evaluate(status, forecast, nowUtc);
            control = await backupReserveController.ApplyAsync(
                decision,
                status,
                nowUtc,
                cancellationToken);

            PrintStatus(status);
            PrintModels(modelForecasts, selection, accuracy);
            PrintForecast(forecast);
            PrintDecision(decision);
            PrintControl(control);

            await auditWriter.AppendAsync(
                status,
                forecast,
                decision,
                control,
                cancellationToken);
            Console.WriteLine($"Audit log:    {settingsProvider.Current.DecisionLogPath}");
            exitCode = control.State == "failed" ? 1 : control.IsSuccess ? 0 : 2;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"EcoFlow forecast failed: {error.Message}");
            failure = $"Forecast or control failed ({error.GetType().Name}).";
        }

        var notification = new ForecastNotification
        {
            ForecastDate = forecast?.ForecastDate ?? GetTomorrowDate(),
            SelectedModel = selection?.Source,
            ExpectedGenerationKwh = forecast?.ExpectedGenerationKwh,
            PreviousBackupReserve = status?.BackupReserve,
            TargetBackupReserve = decision?.RecommendedBackupReserve,
            ConfirmedBackupReserve = control?.IsSuccess is true
                ? control.ReadbackBackupReserve
                : null,
            Success = exitCode == 0,
            Outcome = failure ?? $"Control {control?.State ?? "failed"}: {control?.Reason ?? "no outcome"}",
        };
        var delivery = await NotifySafelyAsync(notification);
        Console.WriteLine($"Telegram:     {delivery.State} — {delivery.Reason}");
        return exitCode;
    }

    private DateOnly GetTomorrowDate()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settingsProvider.Current.TimeZone);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        return DateOnly.FromDateTime(localNow.Date.AddDays(1));
    }

    private async Task<NotificationDeliveryOutcome> NotifySafelyAsync(
        ForecastNotification notification)
    {
        try
        {
            return await notifier.NotifyAsync(notification, CancellationToken.None);
        }
        catch (Exception error)
        {
            return new NotificationDeliveryOutcome
            {
                State = "failed",
                Reason = $"Telegram notifier failed ({error.GetType().Name}).",
            };
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
        Console.WriteLine($"Backup reserve: {Format(status.BackupReserve, "%")} ({FormatSwitch(status.BackupReserveEnabled)})");
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
        Console.WriteLine($"Mode:         {(decision.DryRun ? "DRY-RUN" : "ACTIVE CONTROL")}");
        Console.WriteLine($"Actionable:   {decision.IsActionable}");
        Console.WriteLine(
            $"Recommendation: {(decision.RecommendedBackupReserve is null ? "none" : $"backup reserve {decision.RecommendedBackupReserve}%")}");
        Console.WriteLine($"Reason:       {decision.Reason}");
    }

    private static void PrintControl(BackupReserveControlOutcome control)
    {
        Console.WriteLine($"Control:      {control.State}");
        Console.WriteLine($"Control note: {control.Reason}");
        if (control.ReadbackBackupReserve is not null)
        {
            Console.WriteLine($"Readback:     {control.ReadbackBackupReserve:0}%");
        }
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
