using System.IO;
using System.Text.Json;
using Shush.Core.Models;

namespace Shush.Core.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON. The file path is injectable
/// so tests can round-trip against a temp file instead of the real profile.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public SettingsService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Shush",
            "settings.json");
    }

    /// <summary>Absolute path of the backing settings file.</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Loads settings, returning defaults if the file is missing or unreadable.
    /// Never throws for a missing/corrupt file.
    /// </summary>
    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_filePath);
            settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }

        // Guard against tampered/out-of-range values.
        settings.Decibels = GainMath.ClampDecibels(settings.Decibels);
        return settings;
    }

    /// <summary>Persists settings, creating the containing directory if needed.</summary>
    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }
}
