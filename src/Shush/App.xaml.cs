using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;
using Shush.Core.Services;
using Shush.ViewModels;

namespace Shush;

/// <summary>
/// Application shell: single-instance guard, tray icon, and window/tray lifetime.
/// The window hides to the tray on close; the app exits only via the tray "Exit" item.
/// </summary>
public partial class App : Application
{
    private const string MutexName = "Shush.SingleInstance.9F2C1E7A-2B3D-4C5E-8A1F-6D7E8F901234";
    private const string ShowEventName = "Shush.ShowWindow.9F2C1E7A-2B3D-4C5E-8A1F-6D7E8F901234";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private Thread? _showListener;
    private volatile bool _listening = true;

    private SettingsService? _settingsService;
    private AudioAttenuationService? _audioService;
    private StartupService? _startupService;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private TaskbarIcon? _trayIcon;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Already running: ask the live instance to surface its window, then quit.
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showListener = new Thread(ShowEventLoop) { IsBackground = true, Name = "Shush.ShowListener" };
        _showListener.Start();

        var startMinimized = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

        _settingsService = new SettingsService();
        var settings = _settingsService.Load();
        _startupService = new StartupService("Shush");
        _audioService = new AudioAttenuationService();
        _viewModel = new MainViewModel(settings, _settingsService, _audioService, _startupService);

        _window = new MainWindow { DataContext = _viewModel };
        _window.Closing += OnWindowClosing;

        CreateTrayIcon();

        _viewModel.Initialize();

        if (!startMinimized)
        {
            ShowMainWindow();
        }
    }

    private static void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var handle))
            {
                handle.Set();
                handle.Dispose();
            }
        }
        catch
        {
            // Nothing we can do; the other instance simply won't pop up.
        }
    }

    private void CreateTrayIcon()
    {
        var menu = new ContextMenu();

        var open = new MenuItem { Header = "Open Shush" };
        open.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(open);

        var mute = new MenuItem { Header = "Mute", IsCheckable = true };
        mute.SetBinding(MenuItem.IsCheckedProperty, new Binding(nameof(MainViewModel.IsMuted))
        {
            Source = _viewModel,
            Mode = BindingMode.TwoWay
        });
        menu.Items.Add(mute);

        var startup = new MenuItem { Header = "Launch at startup", IsCheckable = true };
        startup.SetBinding(MenuItem.IsCheckedProperty, new Binding(nameof(MainViewModel.LaunchAtStartup))
        {
            Source = _viewModel,
            Mode = BindingMode.TwoWay
        });
        menu.Items.Add(startup);

        menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitApplication();
        menu.Items.Add(exit);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Shush \u2014 volume attenuator",
            ContextMenu = menu,
            IconSource = TryFindResource("AppIcon") as ImageSource
        };
        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowEventLoop()
    {
        while (_listening)
        {
            try
            {
                if (_showEvent is not null && _showEvent.WaitOne(500))
                {
                    Dispatcher.BeginInvoke(new Action(ShowMainWindow));
                }
            }
            catch
            {
                break;
            }
        }
    }

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        // Hide to tray instead of exiting.
        e.Cancel = true;
        _window?.Hide();
        _viewModel?.FlushSave();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _viewModel?.FlushSave();
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _listening = false;
        try
        {
            _showEvent?.Set();
        }
        catch
        {
            // ignored
        }

        _viewModel?.FlushSave();
        _trayIcon?.Dispose();
        _audioService?.Dispose();

        try
        {
            _showEvent?.Dispose();
        }
        catch
        {
            // ignored
        }

        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch
        {
            // Not owned (second instance path); ignore.
        }

        try
        {
            _instanceMutex?.Dispose();
        }
        catch
        {
            // ignored
        }

        base.OnExit(e);
    }
}

