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
    public TimeSpan MaximumStatusAge { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan MaximumForecastAge { get; init; } = TimeSpan.FromHours(1);
    public double ModerateExpectedGenerationKwh { get; init; } = 1.5;
    public double HighExpectedGenerationKwh { get; init; } = 3.0;
    public int HighSolarChargeLimit { get; init; } = 70;
    public int ModerateSolarChargeLimit { get; init; } = 85;
    public int LowSolarChargeLimit { get; init; } = 100;
    public string DecisionLogPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ecoflow-energy-manager",
        "decisions.jsonl");

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
        MaximumStatusAge = TimeSpan.FromMinutes(
            ReadDouble("ECOFLOW_MAX_STATUS_AGE_MINUTES", 2, 0.1, 60)),
        MaximumForecastAge = TimeSpan.FromMinutes(
            ReadDouble("ECOFLOW_MAX_FORECAST_AGE_MINUTES", 60, 1, 1440)),
        ModerateExpectedGenerationKwh = ReadDouble(
            "ECOFLOW_MODERATE_GENERATION_KWH", 1.5, 0, 1000),
        HighExpectedGenerationKwh = ReadDouble(
            "ECOFLOW_HIGH_GENERATION_KWH", 3.0, 0, 1000),
        HighSolarChargeLimit = ReadInt("ECOFLOW_HIGH_SOLAR_LIMIT", 70, 50, 100),
        ModerateSolarChargeLimit = ReadInt("ECOFLOW_MODERATE_SOLAR_LIMIT", 85, 50, 100),
        LowSolarChargeLimit = ReadInt("ECOFLOW_LOW_SOLAR_LIMIT", 100, 50, 100),
        DecisionLogPath = ReadString(
            "ECOFLOW_DECISION_LOG",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ecoflow-energy-manager",
                "decisions.jsonl")),
    }.Validate();

    private EnergyManagerOptions Validate()
    {
        if (HighExpectedGenerationKwh <= ModerateExpectedGenerationKwh)
        {
            throw new InvalidOperationException(
                "ECOFLOW_HIGH_GENERATION_KWH must be greater than ECOFLOW_MODERATE_GENERATION_KWH.");
        }

        if (HighSolarChargeLimit > ModerateSolarChargeLimit ||
            ModerateSolarChargeLimit > LowSolarChargeLimit)
        {
            throw new InvalidOperationException(
                "Charge limits must increase from high-solar to low-solar conditions.");
        }

        _ = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        return this;
    }

    private static string ReadString(string name, string fallback) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
            ? fallback
            : Environment.GetEnvironmentVariable(name)!.Trim();

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
}
