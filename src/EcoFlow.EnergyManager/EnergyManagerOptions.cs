using System.Globalization;

namespace EcoFlow.EnergyManager;

public sealed record EnergyManagerOptions
{
    public double Latitude { get; init; } = 46.4775;
    public double Longitude { get; init; } = 30.7326;
    public string TimeZone { get; init; } = "Europe/Kyiv";
    public double NominalPowerKw { get; init; } = 1.0;
    public double PanelTiltDegrees { get; init; } = 45;
    public double PanelAzimuthDegrees { get; init; } = 0;
    public double SystemEfficiency { get; init; } = 0.85;
    public double TemperatureCoefficientPerCelsius { get; init; } = -0.004;
    public int ForecastRunHourLocal { get; init; } = 23;
    public int AccuracyWindowDays { get; init; } = 60;
    public int MinimumAccuracySamples { get; init; } = 24;
    public string[] WeatherModels { get; init; } =
    [
        "ecmwf_ifs025",
        "icon_seamless",
        "gfs_seamless",
        "ecmwf_aifs025_single",
    ];
    public string PostgresConnectionString { get; init; } = string.Empty;
    public TimeSpan MaximumStatusAge { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan MaximumForecastAge { get; init; } = TimeSpan.FromHours(1);
    public bool ControlEnabled { get; init; }
    public int MinimumAllowedBackupReserve { get; init; } = 20;
    public int MaximumAllowedBackupReserve { get; init; } = 100;
    public TimeSpan MinimumControlInterval { get; init; } = TimeSpan.FromHours(1);
    public string ManualOverrideFile { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ecoflow-energy-manager",
        "manual-override");
    public string ControlStatePath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ecoflow-energy-manager",
        "control-state.json");
    public string DecisionLogPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ecoflow-energy-manager",
        "decisions.jsonl");
    public string TelegramNotificationCommand { get; init; } = string.Empty;
    public TimeSpan TelegramNotificationTimeout { get; init; } = TimeSpan.FromSeconds(120);

    public static EnergyManagerOptions FromEnvironment() => new EnergyManagerOptions
    {
        Latitude = ReadDouble("ECOFLOW_LATITUDE", 46.4775, -90, 90),
        Longitude = ReadDouble("ECOFLOW_LONGITUDE", 30.7326, -180, 180),
        TimeZone = ReadString("ECOFLOW_TIMEZONE", "Europe/Kyiv"),
        NominalPowerKw = ReadDouble("ECOFLOW_NOMINAL_POWER_KW", 1.0, 0.01, 1000),
        PanelTiltDegrees = ReadDouble("ECOFLOW_PANEL_TILT", 45, 0, 90),
        PanelAzimuthDegrees = ReadDouble("ECOFLOW_PANEL_AZIMUTH", 0, -180, 180),
        SystemEfficiency = ReadDouble("ECOFLOW_SYSTEM_EFFICIENCY", 0.85, 0.1, 1),
        TemperatureCoefficientPerCelsius = ReadDouble(
            "ECOFLOW_TEMPERATURE_COEFFICIENT", -0.004, -0.02, 0),
        ForecastRunHourLocal = ReadInt("ECOFLOW_FORECAST_RUN_HOUR", 23, 0, 23),
        AccuracyWindowDays = ReadInt("ECOFLOW_ACCURACY_WINDOW_DAYS", 60, 7, 365),
        MinimumAccuracySamples = ReadInt("ECOFLOW_MIN_ACCURACY_SAMPLES", 24, 1, 10000),
        WeatherModels = ReadList(
            "ECOFLOW_WEATHER_MODELS",
            ["ecmwf_ifs025", "icon_seamless", "gfs_seamless", "ecmwf_aifs025_single"]),
        PostgresConnectionString = ReadRequiredString("ECOFLOW_POSTGRES_CONNECTION"),
        MaximumStatusAge = TimeSpan.FromMinutes(
            ReadDouble("ECOFLOW_MAX_STATUS_AGE_MINUTES", 2, 0.1, 60)),
        MaximumForecastAge = TimeSpan.FromMinutes(
            ReadDouble("ECOFLOW_MAX_FORECAST_AGE_MINUTES", 60, 1, 1440)),
        ControlEnabled = ReadBool("ECOFLOW_CONTROL_ENABLED", false),
        MinimumAllowedBackupReserve = ReadInt(
            "ECOFLOW_CONTROL_MIN_BACKUP_RESERVE", 20, 20, 100),
        MaximumAllowedBackupReserve = ReadInt(
            "ECOFLOW_CONTROL_MAX_BACKUP_RESERVE", 100, 20, 100),
        MinimumControlInterval = TimeSpan.FromMinutes(
            ReadDouble("ECOFLOW_CONTROL_MIN_INTERVAL_MINUTES", 60, 1, 1440)),
        ManualOverrideFile = ReadString(
            "ECOFLOW_MANUAL_OVERRIDE_FILE",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ecoflow-energy-manager",
                "manual-override")),
        ControlStatePath = ReadString(
            "ECOFLOW_CONTROL_STATE_PATH",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ecoflow-energy-manager",
                "control-state.json")),
        DecisionLogPath = ReadString(
            "ECOFLOW_DECISION_LOG",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ecoflow-energy-manager",
                "decisions.jsonl")),
        TelegramNotificationCommand = ReadString("ECOFLOW_TELEGRAM_COMMAND", string.Empty),
        TelegramNotificationTimeout = TimeSpan.FromSeconds(
            ReadDouble("ECOFLOW_TELEGRAM_TIMEOUT_SECONDS", 120, 5, 300)),
    }.Validate();

    public EnergyManagerOptions Validate()
    {
        if (MinimumAllowedBackupReserve > MaximumAllowedBackupReserve)
        {
            throw new InvalidOperationException(
                "ECOFLOW_CONTROL_MIN_BACKUP_RESERVE must not exceed " +
                "ECOFLOW_CONTROL_MAX_BACKUP_RESERVE.");
        }

        _ = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

        if (WeatherModels.Length == 0)
        {
            throw new InvalidOperationException("At least one weather model must be configured.");
        }

        return this;
    }

    private static string ReadString(string name, string fallback) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
            ? fallback
            : Environment.GetEnvironmentVariable(name)!.Trim();

    private static string ReadRequiredString(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} is required.")
            : value.Trim();
    }

    private static string[] ReadList(string name, string[] fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static double ReadDouble(
        string name,
        double fallback,
        double minimum,
        double maximum)
    {
        var text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"{name} must be a number from {minimum} to {maximum}.");
        }

        return value;
    }

    private static int ReadInt(string name, int fallback, int minimum, int maximum)
    {
        var text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"{name} must be an integer from {minimum} to {maximum}.");
        }

        return value;
    }

    private static bool ReadBool(string name, bool fallback)
    {
        var text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        return bool.TryParse(text, out var value)
            ? value
            : throw new InvalidOperationException($"{name} must be true or false.");
    }
}
