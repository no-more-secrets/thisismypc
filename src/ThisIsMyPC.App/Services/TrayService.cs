using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Platform;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Owns the system tray icon (9-1). Created once at startup; the icon's visibility
/// follows the trayMode setting live via SettingChanged. Menu actions are delegates
/// supplied by the host so this class stays free of view-model knowledge. The
/// Apply Pending item tracks IPendingChangesService.PendingCount (the Win32 popup is
/// rebuilt from current NativeMenuItem state at show time, so property updates are
/// picked up; NeedsUpdate never fires on Windows).
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IPendingChangesService _pendingChanges;
    private readonly Action _openWindow;
    private readonly Action _applyPending;
    private readonly Action _exit;

    private TrayIcon? _trayIcon;
    private NativeMenuItem? _applyItem;
    private bool _iconLoadFailed;

    public TrayService(
        ISettingsService settings,
        IPendingChangesService pendingChanges,
        Action openWindow,
        Action applyPending,
        Action exit)
    {
        _settings = settings;
        _pendingChanges = pendingChanges;
        _openWindow = openWindow;
        _applyPending = applyPending;
        _exit = exit;

        _settings.SettingChanged += OnSettingChanged;
        _pendingChanges.PropertyChanged += OnPendingChanged;
        SyncVisibility();
    }

    /// <summary>False when tray mode is off OR the icon could not be created; hide-to-tray must not engage then.</summary>
    public bool IsTrayActive => _trayIcon?.IsVisible == true;

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e is { Scope: SettingChangedEventArgs.AppScope, Key: AppSettingKeys.TrayMode })
            Avalonia.Threading.Dispatcher.UIThread.Post(SyncVisibility);
    }

    private void OnPendingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IPendingChangesService.PendingCount))
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshApplyItem);
    }

    private void SyncVisibility()
    {
        var enabled = _settings.GetAppBool(AppSettingKeys.TrayMode, false);
        if (enabled && !_iconLoadFailed)
        {
            _trayIcon ??= CreateIcon();
            if (_trayIcon is not null)
                _trayIcon.IsVisible = true;
        }
        else if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
        }
    }

    private TrayIcon? CreateIcon()
    {
        WindowIcon windowIcon;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://ThisIsMyPC.App/Assets/avalonia-logo.ico"));
            windowIcon = new WindowIcon(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            // Without an icon the Win32 tray never materializes; report and keep
            // IsTrayActive false so hide-to-tray falls back to terminating.
            _iconLoadFailed = true;
            Serilog.Log.Error(ex, "Tray icon asset failed to load; tray mode is unavailable this session");
            return null;
        }

        var icon = new TrayIcon
        {
            ToolTipText = "ThisIsMyPC",
            Icon = windowIcon,
        };

        var open = new NativeMenuItem("Open ThisIsMyPC");
        open.Click += (_, _) => _openWindow();

        _applyItem = new NativeMenuItem("Apply Pending Changes");
        _applyItem.Click += (_, _) => _applyPending();

        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => _exit();

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(_applyItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);
        icon.Menu = menu;
        icon.Clicked += (_, _) => _openWindow();

        RefreshApplyItem();
        return icon;
    }

    private void RefreshApplyItem()
    {
        if (_applyItem is null)
            return;
        var count = _pendingChanges.PendingCount;
        _applyItem.IsEnabled = count > 0;
        _applyItem.Header = count > 0 ? $"Apply Pending Changes ({count})" : "Apply Pending Changes";
    }

    public void Dispose()
    {
        _settings.SettingChanged -= OnSettingChanged;
        _pendingChanges.PropertyChanged -= OnPendingChanged;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
