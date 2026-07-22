using System.IO;
using Shush.Core.Models;
using Shush.Core.Services;

namespace Shush.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _directory;

    public SettingsServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ShushTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    private string PathFor(string name) => Path.Combine(_directory, name);

    [Fact]
    public void Save_ThenLoad_RoundTripsAllFields()
    {
        var path = PathFor("settings.json");
        var service = new SettingsService(path);
        var original = new AppSettings
        {
            TargetDeviceId = "{0.0.0.00000000}.{abc-123}",
            TargetDeviceName = "USB-C DAC",
            Decibels = -33.5,
            Mute = true,
            LaunchAtStartup = true
        };

        service.Save(original);
        var loaded = service.Load();

        Assert.Equal(original.TargetDeviceId, loaded.TargetDeviceId);
        Assert.Equal(original.TargetDeviceName, loaded.TargetDeviceName);
        Assert.Equal(original.Decibels, loaded.Decibels);
        Assert.Equal(original.Mute, loaded.Mute);
        Assert.Equal(original.LaunchAtStartup, loaded.LaunchAtStartup);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var service = new SettingsService(PathFor("does-not-exist.json"));

        var loaded = service.Load();

        Assert.Null(loaded.TargetDeviceId);
        Assert.Equal(-20.0, loaded.Decibels);
        Assert.False(loaded.Mute);
        Assert.False(loaded.LaunchAtStartup);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsWithoutThrowing()
    {
        var path = PathFor("corrupt.json");
        File.WriteAllText(path, "{ this is not valid json ]");
        var service = new SettingsService(path);

        var loaded = service.Load();

        Assert.Equal(-20.0, loaded.Decibels);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var path = Path.Combine(_directory, "nested", "deeper", "settings.json");
        var service = new SettingsService(path);

        service.Save(new AppSettings());

        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData(999.0, 0.0)]
    [InlineData(-999.0, -60.0)]
    public void Load_ClampsOutOfRangeDecibels(double stored, double expected)
    {
        var path = PathFor("clamp.json");
        File.WriteAllText(path, $"{{ \"Decibels\": {stored.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}");
        var service = new SettingsService(path);

        var loaded = service.Load();

        Assert.Equal(expected, loaded.Decibels);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
