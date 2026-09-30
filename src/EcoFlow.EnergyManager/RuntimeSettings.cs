using System.Text.Json;

namespace EcoFlow.EnergyManager;

public sealed record RuntimeSettings
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string TimeZone { get; init; } = string.Empty;
    public double NominalPowerKw { get; init; }
    public double PanelTiltDegrees { get; init; }
    public double PanelAzimuthDegrees { get; init; }
    public double SystemEfficiency { get; init; }
    public double TemperatureCoefficientPerCelsius { get; init; }
    public int ForecastRunHourLocal { get; init; }
    public double MaximumStatusAgeMinutes { get; init; }
    public double MaximumForecastAgeMinutes { get; init; }

    public static RuntimeSettings FromOptions(EnergyManagerOptions options) => new()
    {
        Latitude = options.Latitude,
        Longitude = options.Longitude,
        TimeZone = options.TimeZone,
        NominalPowerKw = options.NominalPowerKw,
        PanelTiltDegrees = options.PanelTiltDegrees,
        PanelAzimuthDegrees = options.PanelAzimuthDegrees,
        SystemEfficiency = options.SystemEfficiency,
        TemperatureCoefficientPerCelsius = options.TemperatureCoefficientPerCelsius,
        ForecastRunHourLocal = options.ForecastRunHourLocal,
        MaximumStatusAgeMinutes = options.MaximumStatusAge.TotalMinutes,
        MaximumForecastAgeMinutes = options.MaximumForecastAge.TotalMinutes,
    };

    public EnergyManagerOptions ApplyTo(EnergyManagerOptions options) => options with
    {
        Latitude = Latitude,
        Longitude = Longitude,
        TimeZone = TimeZone.Trim(),
        NominalPowerKw = NominalPowerKw,
        PanelTiltDegrees = PanelTiltDegrees,
        PanelAzimuthDegrees = PanelAzimuthDegrees,
        SystemEfficiency = SystemEfficiency,
        TemperatureCoefficientPerCelsius = TemperatureCoefficientPerCelsius,
        ForecastRunHourLocal = ForecastRunHourLocal,
        MaximumStatusAge = TimeSpan.FromMinutes(MaximumStatusAgeMinutes),
        MaximumForecastAge = TimeSpan.FromMinutes(MaximumForecastAgeMinutes),
    };

    public IReadOnlyDictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        AddRangeError(errors, nameof(Latitude), Latitude, -90, 90);
        AddRangeError(errors, nameof(Longitude), Longitude, -180, 180);
        AddRangeError(errors, nameof(NominalPowerKw), NominalPowerKw, 0.01, 1000);
        AddRangeError(errors, nameof(PanelTiltDegrees), PanelTiltDegrees, 0, 90);
        AddRangeError(errors, nameof(PanelAzimuthDegrees), PanelAzimuthDegrees, -180, 180);
        AddRangeError(errors, nameof(SystemEfficiency), SystemEfficiency, 0.1, 1);
        AddRangeError(errors, nameof(TemperatureCoefficientPerCelsius), TemperatureCoefficientPerCelsius, -0.02, 0);
        AddRangeError(errors, nameof(ForecastRunHourLocal), ForecastRunHourLocal, 0, 23);
        AddRangeError(errors, nameof(MaximumStatusAgeMinutes), MaximumStatusAgeMinutes, 0.1, 60);
        AddRangeError(errors, nameof(MaximumForecastAgeMinutes), MaximumForecastAgeMinutes, 1, 1440);

        if (string.IsNullOrWhiteSpace(TimeZone))
        {
            errors[nameof(TimeZone)] = ["Часовой пояс обязателен."];
        }
        else
        {
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(TimeZone.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
                errors[nameof(TimeZone)] = ["Неизвестный часовой пояс IANA."];
            }
            catch (InvalidTimeZoneException)
            {
                errors[nameof(TimeZone)] = ["Некорректный часовой пояс."];
            }
        }

        return errors;
    }

    private static void AddRangeError(
        IDictionary<string, string[]> errors,
        string name,
        double value,
        double minimum,
        double maximum)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            errors[name] = [$"Допустимый диапазон: {minimum}…{maximum}."];
        }
    }
}

public interface IRuntimeSettingsProvider
{
    EnergyManagerOptions Current { get; }
    RuntimeSettings PublicSettings { get; }
    Task SettingsChanged { get; }
    Task<IReadOnlyDictionary<string, string[]>> UpdateAsync(
        RuntimeSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed class RuntimeSettingsProvider : IRuntimeSettingsProvider, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private EnergyManagerOptions _current;
    private TaskCompletionSource _settingsChanged = CreateChangeSource();

    public RuntimeSettingsProvider(EnergyManagerOptions defaults, string path)
    {
        _path = Path.GetFullPath(path);
        _current = Load(defaults, _path);
    }

    public EnergyManagerOptions Current => Volatile.Read(ref _current);

    public RuntimeSettings PublicSettings => RuntimeSettings.FromOptions(Current);

    public Task SettingsChanged => Volatile.Read(ref _settingsChanged).Task;

    public async Task<IReadOnlyDictionary<string, string[]>> UpdateAsync(
        RuntimeSettings settings,
        CancellationToken cancellationToken = default)
    {
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            return errors;
        }

        await _updateLock.WaitAsync(cancellationToken);
        try
        {
            var next = settings.ApplyTo(Current);
            var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException(
                "Settings path has no parent directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = _path + ".tmp";
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporaryPath, _path, true);
            Volatile.Write(ref _current, next);
            Interlocked.Exchange(ref _settingsChanged, CreateChangeSource()).TrySetResult();
            return new Dictionary<string, string[]>();
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public void Dispose() => _updateLock.Dispose();

    private static TaskCompletionSource CreateChangeSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static EnergyManagerOptions Load(EnergyManagerOptions defaults, string path)
    {
        if (!File.Exists(path))
        {
            return defaults;
        }

        RuntimeSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<RuntimeSettings>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException("Settings file is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException($"Settings file is invalid JSON: {path}", error);
        }

        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Settings file is invalid: {string.Join("; ", errors.SelectMany(item => item.Value.Select(message => $"{item.Key}: {message}")))}");
        }

        return settings.ApplyTo(defaults);
    }
}

public sealed class FixedRuntimeSettingsProvider(EnergyManagerOptions options) : IRuntimeSettingsProvider
{
    public EnergyManagerOptions Current { get; } = options;
    public RuntimeSettings PublicSettings => RuntimeSettings.FromOptions(Current);
    public Task SettingsChanged { get; } = Task.Delay(Timeout.InfiniteTimeSpan);

    public Task<IReadOnlyDictionary<string, string[]>> UpdateAsync(
        RuntimeSettings settings,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
