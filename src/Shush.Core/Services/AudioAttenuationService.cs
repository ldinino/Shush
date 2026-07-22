using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Shush.Core.Models;
using Timer = System.Timers.Timer;

namespace Shush.Core.Services;

/// <summary>
/// Continuously forces the per-app session volume of every stream on a chosen render
/// device down to a slider-controlled gain, giving attenuation finer than the Windows
/// volume UI exposes. The master/system volume is never touched.
///
/// Threading: all Core Audio (COM) objects are created and used on the thread that calls
/// <see cref="Start"/> (the WPF UI thread). A background poll timer and the COM device/session
/// notifications only ever marshal work back onto that thread via the captured
/// <see cref="SynchronizationContext"/>, so there is no cross-apartment access.
/// </summary>
public sealed class AudioAttenuationService : IMMNotificationClient, IDisposable
{
    private const int TickIntervalMs = 250;
    private const int RefreshEveryTicks = 8; // Full session re-enumeration roughly every 2s.

    private SynchronizationContext? _context;
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private AudioSessionManager? _sessionManager;
    private Timer? _timer;
    private int _tickCounter;
    private int _tickPending;

    private string? _targetDeviceId;
    private float _gain = 1f;
    private bool _mute;
    private bool _started;
    private bool _disposed;

    /// <summary>Whether the target device is currently present and being attenuated.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>Friendly name of the connected device, or null when disconnected.</summary>
    public string? ConnectedDeviceName { get; private set; }

    /// <summary>Endpoint ID currently targeted (null == follow default device).</summary>
    public string? TargetDeviceId => _targetDeviceId;

    /// <summary>Raised (on the UI thread) whenever the connection state changes.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Raised (on the UI thread) when the set of available devices may have changed.</summary>
    public event EventHandler? DevicesChanged;

    private float EffectiveGain => _mute ? 0f : _gain;

    /// <summary>
    /// Begins attenuation. Must be called on a UI thread (a <see cref="SynchronizationContext"/>
    /// is captured for marshaling COM notifications).
    /// </summary>
    public void Start(string? initialTargetDeviceId)
    {
        if (_started)
        {
            return;
        }

        _context = SynchronizationContext.Current
                   ?? throw new InvalidOperationException(
                       "AudioAttenuationService.Start must be called on a thread with a SynchronizationContext (the UI thread).");
        _targetDeviceId = initialTargetDeviceId;
        _enumerator = new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(this);
        _started = true;

        Attach();

        _timer = new Timer(TickIntervalMs) { AutoReset = true };
        _timer.Elapsed += OnTimerElapsed;
        _timer.Start();
    }

    /// <summary>Sets the linear gain applied to every session (0..1).</summary>
    public void SetGain(float linearGain)
    {
        _gain = Math.Clamp(linearGain, 0f, 1f);
        PostOrRun(ApplyToSessions);
    }

    /// <summary>Toggles full mute (forces effective gain to zero).</summary>
    public void SetMute(bool mute)
    {
        _mute = mute;
        PostOrRun(ApplyToSessions);
    }

    /// <summary>Locks attenuation to a specific endpoint ID (null == follow default device).</summary>
    public void SetTargetDevice(string? deviceId)
    {
        PostOrRun(() =>
        {
            if (_targetDeviceId == deviceId)
            {
                return;
            }

            _targetDeviceId = deviceId;
            Detach();
            Attach();
        });
    }

    /// <summary>Returns the currently active render endpoints.</summary>
    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        var results = new List<AudioDeviceInfo>();
        var enumerator = _enumerator;
        if (enumerator is null)
        {
            return results;
        }

        try
        {
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            for (var i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                try
                {
                    results.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
                }
                catch
                {
                    // Skip a device that vanished mid-enumeration.
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch
        {
            // Enumeration can fail transiently while devices are changing.
        }

        return results;
    }

    /// <summary>Returns the current default multimedia render endpoint, if any.</summary>
    public AudioDeviceInfo? GetDefaultRenderDevice()
    {
        var enumerator = _enumerator;
        if (enumerator is null)
        {
            return null;
        }

        try
        {
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                return null;
            }

            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return new AudioDeviceInfo(device.ID, device.FriendlyName);
        }
        catch
        {
            return null;
        }
    }

    private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        // Drop overlapping ticks if the UI thread is momentarily busy.
        if (Interlocked.CompareExchange(ref _tickPending, 1, 0) != 0)
        {
            return;
        }

        Post(() =>
        {
            try
            {
                OnTick();
            }
            finally
            {
                Interlocked.Exchange(ref _tickPending, 0);
            }
        });
    }

    private void OnTick()
    {
        if (_disposed || !_started)
        {
            return;
        }

        if (!IsConnected)
        {
            Attach();
            return;
        }

        if (!IsDeviceUsable(_device))
        {
            SetDisconnected();
            return;
        }

        _tickCounter++;
        if (_tickCounter % RefreshEveryTicks == 0)
        {
            RefreshAndApply();
        }
        else
        {
            ApplyToSessions();
        }
    }

    private void Attach()
    {
        if (_enumerator is null || _disposed)
        {
            return;
        }

        MMDevice? device = null;
        try
        {
            if (!string.IsNullOrEmpty(_targetDeviceId))
            {
                try
                {
                    device = _enumerator.GetDevice(_targetDeviceId);
                }
                catch
                {
                    device = null; // Locked device is not present right now.
                }
            }
            else if (_enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }

            if (device is null || !IsDeviceUsable(device))
            {
                device?.Dispose();
                SetDisconnected();
                return;
            }

            _device = device;
            _sessionManager = device.AudioSessionManager;
            _sessionManager.OnSessionCreated += OnSessionCreated;
            ApplyToSessions();
            RaiseStatusIfChanged(connected: true, SafeName(device));
        }
        catch
        {
            try
            {
                device?.Dispose();
            }
            catch
            {
                // ignored
            }

            _device = null;
            _sessionManager = null;
            SetDisconnected();
        }
    }

    private void Detach()
    {
        if (_sessionManager is not null)
        {
            try
            {
                _sessionManager.OnSessionCreated -= OnSessionCreated;
            }
            catch
            {
                // ignored
            }

            try
            {
                _sessionManager.Dispose();
            }
            catch
            {
                // ignored
            }

            _sessionManager = null;
        }

        if (_device is not null)
        {
            try
            {
                _device.Dispose();
            }
            catch
            {
                // ignored
            }

            _device = null;
        }
    }

    private void SetDisconnected()
    {
        Detach();
        RaiseStatusIfChanged(connected: false, name: null);
    }

    private void RefreshAndApply()
    {
        var manager = _sessionManager;
        if (manager is null)
        {
            return;
        }

        try
        {
            manager.RefreshSessions();
        }
        catch
        {
            // Enumeration will be retried next tick.
        }

        ApplyToSessions();
    }

    private void ApplyToSessions()
    {
        var manager = _sessionManager;
        if (manager is null)
        {
            return;
        }

        try
        {
            var sessions = manager.Sessions;
            if (sessions is null)
            {
                return;
            }

            var effective = EffectiveGain;
            var count = sessions.Count;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    var session = sessions[i];
                    if (session.State == AudioSessionState.AudioSessionStateExpired)
                    {
                        continue;
                    }

                    session.SimpleAudioVolume.Volume = effective;
                }
                catch
                {
                    // A session can expire between enumeration and assignment; ignore.
                }
            }
        }
        catch
        {
            // Device/manager likely went away; the next tick's validity check will detach.
        }
    }

    private void OnSessionCreated(object? sender, IAudioSessionControl newSession)
    {
        // Fires on a COM thread. Marshal to the UI thread and attenuate the new session
        // immediately so there is no audible flash of full volume.
        Post(() =>
        {
            if (IsConnected)
            {
                RefreshAndApply();
            }
        });
    }

    private static bool IsDeviceUsable(MMDevice? device)
    {
        try
        {
            return device is not null && device.State == DeviceState.Active;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeName(MMDevice device)
    {
        try
        {
            return device.FriendlyName;
        }
        catch
        {
            return "audio device";
        }
    }

    private void RaiseStatusIfChanged(bool connected, string? name)
    {
        var changed = connected != IsConnected || name != ConnectedDeviceName;
        IsConnected = connected;
        ConnectedDeviceName = name;
        if (changed)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnDeviceTopologyChanged()
    {
        Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            DevicesChanged?.Invoke(this, EventArgs.Empty);

            if (IsConnected)
            {
                if (!IsDeviceUsable(_device))
                {
                    SetDisconnected();
                }
            }
            else
            {
                Attach();
            }
        });
    }

    private void Post(Action action)
    {
        _context?.Post(_ =>
        {
            if (!_disposed)
            {
                action();
            }
        }, null);
    }

    private void PostOrRun(Action action)
    {
        if (_disposed)
        {
            return;
        }

        if (SynchronizationContext.Current == _context)
        {
            action();
        }
        else
        {
            Post(action);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_timer is not null)
            {
                _timer.Elapsed -= OnTimerElapsed;
                _timer.Stop();
                _timer.Dispose();
            }
        }
        catch
        {
            // ignored
        }

        _timer = null;

        try
        {
            _enumerator?.UnregisterEndpointNotificationCallback(this);
        }
        catch
        {
            // ignored
        }

        Detach();

        try
        {
            _enumerator?.Dispose();
        }
        catch
        {
            // ignored
        }

        _enumerator = null;
    }

    // ----- IMMNotificationClient (device hotplug / default-device changes) -----

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => OnDeviceTopologyChanged();

    public void OnDeviceAdded(string pwstrDeviceId) => OnDeviceTopologyChanged();

    public void OnDeviceRemoved(string deviceId) => OnDeviceTopologyChanged();

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => OnDeviceTopologyChanged();

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // Not relevant to attenuation.
    }
}
