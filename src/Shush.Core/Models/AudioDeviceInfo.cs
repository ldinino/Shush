namespace Shush.Core.Models;

/// <summary>
/// Lightweight, UI-friendly description of an audio render endpoint.
/// Equality is by <see cref="Id"/> so it round-trips through a WPF ComboBox selection.
/// </summary>
public sealed class AudioDeviceInfo : IEquatable<AudioDeviceInfo>
{
    public AudioDeviceInfo(string id, string name, bool isConnected = true)
    {
        Id = id;
        Name = name;
        IsConnected = isConnected;
    }

    /// <summary>Windows endpoint ID (stable across unplug/replug for the same physical device).</summary>
    public string Id { get; }

    /// <summary>Friendly device name shown to the user.</summary>
    public string Name { get; }

    /// <summary>False when this entry represents a saved-but-currently-absent device.</summary>
    public bool IsConnected { get; }

    /// <summary>Name as shown in the picker, annotated when the device is missing.</summary>
    public string DisplayName => IsConnected ? Name : $"{Name} (disconnected)";

    public override string ToString() => DisplayName;

    public bool Equals(AudioDeviceInfo? other) => other is not null && other.Id == Id;

    public override bool Equals(object? obj) => Equals(obj as AudioDeviceInfo);

    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
}
