using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Wires the main window's close/minimize behavior to the 9-1 policy. Defaults are
/// stock Windows behavior (terminate on close, taskbar on minimize); the tray paths
/// only engage when the user opted in.
/// </summary>
public sealed class WindowPersistenceController : IDisposable
{
    private readonly Window _window;
    private readonly Action _shutdown;
    private readonly ISettingsService _settings;
    private readonly Func<bool> _trayAvailable;

    private bool _exitRequested;
    private bool _disposed;

    public WindowPersistenceController(
        Window window,
        IClassicDesktopStyleApplicationLifetime desktop,
        ISettingsService settings,
        Func<bool>? trayAvailable = null)
        : this(window, () => desktop.Shutdown(), settings, trayAvailable) { }

    internal WindowPersistenceController(Window window, Action shutdown, ISettingsService settings,
        Func<bool>? trayAvailable = null)
    {
        _window = window;
        _shutdown = shutdown;
        _settings = settings;
        _trayAvailable = trayAvailable ?? (() => true);

        _window.Closing += OnClosing;
        _settings.SettingChanged += OnSettingChanged;
    }

    /// <summary>Tray "Exit": bypass the hide-to-tray interception and really terminate.</summary>
    public void RequestExit()
    {
        _exitRequested = true;
        _shutdown();
    }

    public void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_exitRequested)
            return;

        switch (WindowBehaviorPolicy.DecideClose(_settings))
        {
            // Guard: never hide with no live tray icon; the window would be unreachable.
            case CloseDecision.HideToTray when _trayAvailable():
                e.Cancel = true;
                _window.Hide();
                break;
            case CloseDecision.HideToTray:
                break; // fall through to terminate
            case CloseDecision.Terminate:
            default:
                break; // stock close; zero background footprint
        }
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e is not { Scope: SettingChangedEventArgs.AppScope, Key: AppSettingKeys.TrayMode, Value: "0" })
            return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && !_exitRequested && !_window.IsVisible)
                ShowWindow();
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _window.Closing -= OnClosing;
        _settings.SettingChanged -= OnSettingChanged;
    }
}
