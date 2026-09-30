using Xunit;

namespace EcoFlow.EnergyManager.Tests;

public sealed class ForecastNotificationTests
{
    [Fact]
    public void Format_IncludesRequiredForecastAndControlFields()
    {
        var message = ForecastNotificationFormatter.Format(new ForecastNotification
        {
            ForecastDate = new DateOnly(2026, 10, 1),
            SelectedModel = "icon_seamless",
            ExpectedGenerationKwh = 4.77,
            PreviousBackupReserve = 80,
            TargetBackupReserve = 40,
            ConfirmedBackupReserve = 40,
            Success = true,
            Outcome = "Control applied: readback confirmed.",
        });

        Assert.Contains("2026-10-01", message);
        Assert.Contains("Модель: icon_seamless", message);
        Assert.Contains("Генерация: 4.77 кВт·ч", message);
        Assert.Contains("Backup Reserve до: 80%", message);
        Assert.Contains("Расчётная цель: 40%", message);
        Assert.Contains("Подтверждено readback: 40%", message);
        Assert.Contains("Результат: УСПЕХ", message);
    }

    [Fact]
    public void Format_FailureUsesAvailableFieldsAndExplicitUnavailableValues()
    {
        var message = ForecastNotificationFormatter.Format(new ForecastNotification
        {
            ForecastDate = new DateOnly(2026, 10, 1),
            PreviousBackupReserve = 40,
            Success = false,
            Outcome = "Forecast or control failed (HttpRequestException).",
        });

        Assert.Contains("Модель: недоступна", message);
        Assert.Contains("Генерация: недоступна", message);
        Assert.Contains("Backup Reserve до: 40%", message);
        Assert.Contains("Расчётная цель: недоступно", message);
        Assert.Contains("Подтверждено readback: недоступно", message);
        Assert.Contains("Результат: ОШИБКА", message);
    }

    [Fact]
    public async Task NotifyAsync_ReturnsSentOnlyForSuccessfulCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var notification = CreateNotification();
        var successful = await new TelegramCommandForecastNotifier(
            "/usr/bin/true",
            TimeSpan.FromSeconds(2)).NotifyAsync(notification);
        var failed = await new TelegramCommandForecastNotifier(
            "/usr/bin/false",
            TimeSpan.FromSeconds(2)).NotifyAsync(notification);

        Assert.Equal("sent", successful.State);
        Assert.Equal("failed", failed.State);
        Assert.Contains("code 1", failed.Reason);
    }

    [Fact]
    public async Task NotifyAsync_DisabledCommandHasDiagnosableOutcome()
    {
        var outcome = await new TelegramCommandForecastNotifier(
            string.Empty,
            TimeSpan.FromSeconds(2)).NotifyAsync(CreateNotification());

        Assert.Equal("disabled", outcome.State);
        Assert.Contains("not configured", outcome.Reason);
    }

    private static ForecastNotification CreateNotification() => new()
    {
        ForecastDate = new DateOnly(2026, 10, 1),
        Success = true,
        Outcome = "test",
    };
}
