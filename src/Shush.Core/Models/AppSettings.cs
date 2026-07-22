namespace Shush.Core.Models;

/// <summary>
/// User-facing, persisted configuration for Shush.
/// Serialized to <c>%LocalAppData%\Shush\settings.json</c>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Endpoint ID of the render device Shush attenuates. Null == follow current default.</summary>
    public string? TargetDeviceId { get; set; }

    /// <summary>Friendly name of the target device, kept for display while it is disconnected.</summary>
    public string? TargetDeviceName { get; set; }

    /// <summary>Attenuation in decibels (0 == no extra attenuation, -60 == quietest).</summary>
    public double Decibels { get; set; } = -20.0;

    /// <summary>When true, output is forced to silence regardless of the slider.</summary>
    public bool Mute { get; set; }

    /// <summary>Whether Shush launches automatically (minimized) when the user signs in.</summary>
    public bool LaunchAtStartup { get; set; }
}
