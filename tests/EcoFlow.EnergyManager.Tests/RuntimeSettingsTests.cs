using Xunit;

namespace EcoFlow.EnergyManager.Tests;

public sealed class RuntimeSettingsTests
{
    [Fact]
    public void Validate_ReturnsRangeAndCrossFieldErrors()
    {
        var settings = RuntimeSettings.FromOptions(new EnergyManagerOptions()) with
        {
            Latitude = 91,
            HighExpectedGenerationKwh = 1,
            ModerateExpectedGenerationKwh = 2,
            HighSolarChargeLimit = 90,
            ModerateSolarChargeLimit = 80,
        };

        var errors = settings.Validate();

        Assert.Contains(nameof(RuntimeSettings.Latitude), errors.Keys);
        Assert.Contains(nameof(RuntimeSettings.HighExpectedGenerationKwh), errors.Keys);
        Assert.Contains(nameof(RuntimeSettings.HighSolarChargeLimit), errors.Keys);
    }

    [Fact]
    public async Task UpdateAsync_PersistsAndReloadsSettingsWithoutPrivateOptions()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ecoflow-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        var defaults = new EnergyManagerOptions
        {
            PostgresConnectionString = "private-connection",
        };

        try
        {
            var changed = RuntimeSettings.FromOptions(defaults) with
            {
                Latitude = 48.25,
                TimeZone = "UTC",
                NominalPowerKw = 2.5,
            };

            using (var provider = new RuntimeSettingsProvider(defaults, path))
            {
                var errors = await provider.UpdateAsync(changed);
                Assert.Empty(errors);
                Assert.Equal(48.25, provider.Current.Latitude);
            }

            using var reloaded = new RuntimeSettingsProvider(defaults, path);
            Assert.Equal(48.25, reloaded.Current.Latitude);
            Assert.Equal("UTC", reloaded.Current.TimeZone);
            Assert.Equal(2.5, reloaded.Current.NominalPowerKw);
            Assert.Equal("private-connection", reloaded.Current.PostgresConnectionString);

            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("private-connection", json, StringComparison.Ordinal);
            Assert.DoesNotContain("postgres", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task UpdateAsync_DoesNotPersistInvalidSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ecoflow-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");

        try
        {
            using var provider = new RuntimeSettingsProvider(new EnergyManagerOptions(), path);
            var invalid = provider.PublicSettings with { SystemEfficiency = 2 };

            var errors = await provider.UpdateAsync(invalid);

            Assert.Contains(nameof(RuntimeSettings.SystemEfficiency), errors.Keys);
            Assert.False(File.Exists(path));
            Assert.Equal(0.85, provider.Current.SystemEfficiency);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
