using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shush.Core.Models;
using Shush.Core.Services;

namespace Shush.ViewModels;

/// <summary>
/// Drives the main window: exposes the dB slider, mute, device picker, startup toggle,
/// and connection status, and pushes changes into the audio/settings/startup services.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly AudioAttenuationService _audio;
    private readonly StartupService _startup;
    private readonly DispatcherTimer _saveTimer;
    private bool _initializing;

    public MainViewModel(
        AppSettings settings,
        SettingsService settingsService,
        AudioAttenuationService audio,
        StartupService startup)
    {
        _settings = settings;
        _settingsService = settingsService;
        _audio = audio;
        _startup = startup;

        // Debounce disk writes so dragging the slider doesn't hammer the file.
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            PersistSettings();
        };

        _audio.StatusChanged += (_, _) => UpdateStatus();
        _audio.DevicesChanged += (_, _) => RefreshDevices();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputSummary))]
    private double _decibels;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputSummary))]
    private bool _isMuted;

    [ObservableProperty]
    private bool _launchAtStartup;

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _devices = new();

    [ObservableProperty]
    private AudioDeviceInfo? _selectedDevice;

    [ObservableProperty]
    private string _statusText = "Starting\u2026";

    [ObservableProperty]
    private bool _isConnected;

    /// <summary>Human-readable readout of the current attenuation.</summary>
    public string OutputSummary => IsMuted
        ? "Muted"
        : $"{GainMath.DecibelsToPercent(Decibels):0.##}%  \u00b7  {Decibels:0.#} dB";

    /// <summary>Wires up the services and loads persisted state. Call on the UI thread.</summary>
    public void Initialize()
    {
        _initializing = true;

        Decibels = GainMath.ClampDecibels(_settings.Decibels);
        IsMuted = _settings.Mute;
        LaunchAtStartup = _startup.IsEnabled();

        // Enumerator must exist before we can list devices.
        _audio.Start(_settings.TargetDeviceId);
        _audio.SetGain(GainMath.DecibelsToLinear(Decibels));
        _audio.SetMute(IsMuted);

        RefreshDevices();

        _initializing = false;
        UpdateStatus();
    }

    /// <summary>Cancels any pending debounce and writes settings immediately.</summary>
    public void FlushSave()
    {
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
        }

        PersistSettings();
    }

    partial void OnDecibelsChanged(double value)
    {
        if (_initializing)
        {
            return;
        }

        _settings.Decibels = value;
        _audio.SetGain(GainMath.DecibelsToLinear(value));
        ScheduleSave();
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (_initializing)
        {
            return;
        }

        _settings.Mute = value;
        _audio.SetMute(value);
        ScheduleSave();
    }

    partial void OnLaunchAtStartupChanged(bool value)
    {
        if (_initializing)
        {
            return;
        }

        try
        {
            _startup.SetEnabled(value);
        }
        catch
        {
            // Registry writes can rarely fail; keep the UI responsive.
        }

        _settings.LaunchAtStartup = value;
        ScheduleSave();
    }

    partial void OnSelectedDeviceChanged(AudioDeviceInfo? value)
    {
        if (_initializing || value is null)
        {
            return;
        }

        _settings.TargetDeviceId = value.Id;
        _settings.TargetDeviceName = value.Name;
        _audio.SetTargetDevice(value.Id);
        ScheduleSave();
        UpdateStatus();
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        // Suppress the SelectedDevice side effect while we rebuild the list.
        var wasInitializing = _initializing;
        _initializing = true;

        var active = _audio.GetRenderDevices();
        Devices.Clear();
        foreach (var device in active)
        {
            Devices.Add(device);
        }

        AudioDeviceInfo? selection = null;
        var targetId = _settings.TargetDeviceId;

        if (!string.IsNullOrEmpty(targetId))
        {
            selection = Devices.FirstOrDefault(d => d.Id == targetId);
            if (selection is null)
            {
                // The locked device is currently absent; show it as disconnected.
                selection = new AudioDeviceInfo(
                    targetId,
                    _settings.TargetDeviceName ?? "Saved device",
                    isConnected: false);
                Devices.Add(selection);
            }
        }
        else
        {
            var defaultDevice = _audio.GetDefaultRenderDevice();
            if (defaultDevice is not null)
            {
                selection = Devices.FirstOrDefault(d => d.Id == defaultDevice.Id);
            }
        }

        SelectedDevice = selection;
        _initializing = wasInitializing;
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void PersistSettings()
    {
        try
        {
            _settingsService.Save(_settings);
        }
        catch
        {
            // Persisting settings is best-effort.
        }
    }

    private void UpdateStatus()
    {
        IsConnected = _audio.IsConnected;
        if (_audio.IsConnected)
        {
            StatusText = $"Connected to {_audio.ConnectedDeviceName}";
        }
        else if (!string.IsNullOrEmpty(_settings.TargetDeviceName))
        {
            StatusText = $"Waiting for {_settings.TargetDeviceName}\u2026";
        }
        else
        {
            StatusText = "Waiting for a device\u2026";
        }
    }
}
