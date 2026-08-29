using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Wires the main window's close/minimize behavior to the 9-1 policy. Defaults are
/// stock Windows behavior (terminate on close, taskbar on minimize) — the tray paths
/// only engage when the user opted in.
/// </summary>
public sealed class WindowPersistenceController : IDisposable
{
    private readonly Window _window;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly ISettingsService _settings;

    private bool _exitRequested;

    public WindowPersistenceController(
        Window window,
        IClassicDesktopStyleApplicationLifetime desktop,
        ISettingsService settings)
    {
        _window = window;
        _desktop = desktop;
        _settings = settings;

        _window.Closing += OnClosing;
        _window.PropertyChanged += OnWindowPropertyChanged;
    }

    /// <summary>Tray "Exit": bypass the hide-to-tray interception and really terminate.</summary>
    public void RequestExit()
    {
        _exitRequested = true;
        _desktop.Shutdown();
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
            case CloseDecision.HideToTray:
                e.Cancel = true;
                _window.Hide();
                break;
            case CloseDecision.MinimizeToTaskbar:
                e.Cancel = true;
                _window.WindowState = WindowState.Minimized;
                break;
            case CloseDecision.Terminate:
            default:
                break; // stock close — zero background footprint
        }
    }

    private void OnWindowPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty)
            return;
        if (e.NewValue is not WindowState.Minimized)
            return;

        if (WindowBehaviorPolicy.DecideMinimize(_settings) == MinimizeDecision.HideToTray)
            _window.Hide();
    }

    public void Dispose()
    {
        _window.Closing -= OnClosing;
        _window.PropertyChanged -= OnWindowPropertyChanged;
    }
}
