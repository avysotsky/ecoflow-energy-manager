using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace EcoFlow.EnergyManager;

public sealed record ForecastNotification
{
    public DateOnly? ForecastDate { get; init; }
    public string? SelectedModel { get; init; }
    public double? ExpectedGenerationKwh { get; init; }
    public double? PreviousBackupReserve { get; init; }
    public int? TargetBackupReserve { get; init; }
    public double? ConfirmedBackupReserve { get; init; }
    public required bool Success { get; init; }
    public required string Outcome { get; init; }
}

public sealed record NotificationDeliveryOutcome
{
    public required string State { get; init; }
    public required string Reason { get; init; }

    public bool IsSuccess => State == "sent";
}

public interface IForecastNotifier
{
    Task<NotificationDeliveryOutcome> NotifyAsync(
        ForecastNotification notification,
        CancellationToken cancellationToken = default);
}

public static class ForecastNotificationFormatter
{
    public static string Format(ForecastNotification notification)
    {
        var result = new StringBuilder();
        result.Append("⚡ EcoFlow прогноз на ")
            .Append(notification.ForecastDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "неизвестную дату")
            .AppendLine();
        result.Append("Модель: ").AppendLine(notification.SelectedModel ?? "недоступна");
        result.Append("Генерация: ")
            .Append(notification.ExpectedGenerationKwh?.ToString("0.00", CultureInfo.InvariantCulture) ?? "недоступна")
            .AppendLine(notification.ExpectedGenerationKwh is null ? string.Empty : " кВт·ч");
        result.Append("Backup Reserve до: ").AppendLine(Percent(notification.PreviousBackupReserve));
        result.Append("Расчётная цель: ").AppendLine(Percent(notification.TargetBackupReserve));
        result.Append("Подтверждено readback: ").AppendLine(Percent(notification.ConfirmedBackupReserve));
        result.Append("Результат: ")
            .Append(notification.Success ? "УСПЕХ" : "ОШИБКА")
            .Append(" — ")
            .Append(notification.Outcome);
        return result.ToString();
    }

    private static string Percent(double? value) => value is null
        ? "недоступно"
        : $"{value.Value.ToString("0", CultureInfo.InvariantCulture)}%";
}

public sealed class TelegramCommandForecastNotifier(
    string? commandPath,
    TimeSpan timeout) : IForecastNotifier
{
    public async Task<NotificationDeliveryOutcome> NotifyAsync(
        ForecastNotification notification,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commandPath))
        {
            return Outcome("disabled", "Telegram notification command is not configured.");
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = commandPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add(ForecastNotificationFormatter.Format(notification));
            if (!process.Start())
            {
                return Outcome("failed", "Telegram command did not start.");
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                return Outcome("failed", "Telegram command timed out.");
            }

            return process.ExitCode == 0
                ? Outcome("sent", "Telegram delivery command completed successfully.")
                : Outcome("failed", $"Telegram delivery command exited with code {process.ExitCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            return Outcome("failed", $"Telegram delivery failed ({error.GetType().Name}).");
        }
    }

    private static NotificationDeliveryOutcome Outcome(string state, string reason) => new()
    {
        State = state,
        Reason = reason,
    };
}
