using Microsoft.Win32;

namespace Shush.Core.Services;

/// <summary>
/// Toggles "launch at sign-in" via the per-user HKCU Run key (no admin rights required).
/// The registered command launches the app minimized so auto-start goes straight to the tray.
/// </summary>
public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName;
    private readonly string _command;

    public StartupService(string valueName, string? command = null)
    {
        _valueName = valueName;
        _command = command ?? $"\"{Environment.ProcessPath}\" --minimized";
    }

    /// <summary>True if a Run entry for this app currently exists.</summary>
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(_valueName) is string value && !string.IsNullOrEmpty(value);
    }

    /// <summary>Adds or removes the Run entry to match <paramref name="enabled"/>.</summary>
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(_valueName, _command, RegistryValueKind.String);
        }
        else if (key.GetValue(_valueName) is not null)
        {
            key.DeleteValue(_valueName, throwOnMissingValue: false);
        }
    }
}
