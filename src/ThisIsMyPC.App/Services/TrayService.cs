using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Owns the system tray icon (9-1). Created once at startup; the icon's visibility
/// follows the trayMode setting live via SettingChanged. Menu actions are delegates
/// supplied by the host so this class stays free of view-model knowledge.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly Func<int> _pendingCount;
    private readonly Action _openWindow;
    private readonly Action _applyPending;
    private readonly Action _exit;

    private TrayIcon? _trayIcon;
    private NativeMenuItem? _applyItem;

    public TrayService(
        ISettingsService settings,
        Func<int> pendingCount,
        Action openWindow,
        Action applyPending,
        Action exit)
    {
        _settings = settings;
        _pendingCount = pendingCount;
        _openWindow = openWindow;
        _applyPending = applyPending;
        _exit = exit;

        _settings.SettingChanged += OnSettingChanged;
        SyncVisibility();
    }

    public bool IsTrayActive => _trayIcon?.IsVisible == true;

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e is { Scope: SettingChangedEventArgs.AppScope, Key: AppSettingKeys.TrayMode })
            Avalonia.Threading.Dispatcher.UIThread.Post(SyncVisibility);
    }

    private void SyncVisibility()
    {
        var enabled = _settings.GetAppBool(AppSettingKeys.TrayMode, false);
        if (enabled)
        {
            _trayIcon ??= CreateIcon();
            _trayIcon.IsVisible = true;
        }
        else if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
        }
    }

    private TrayIcon CreateIcon()
    {
        var icon = new TrayIcon
        {
            ToolTipText = "ThisIsMyPC",
        };

        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://ThisIsMyPC.App/Assets/avalonia-logo.ico"));
            icon.Icon = new WindowIcon(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            Serilog.Log.Warning(ex, "Tray icon asset failed to load; tray will use the default glyph");
        }

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

        menu.NeedsUpdate += (_, _) => RefreshApplyItem();
        RefreshApplyItem();
        return icon;
    }

    private void RefreshApplyItem()
    {
        if (_applyItem is null)
            return;
        var count = _pendingCount();
        _applyItem.IsEnabled = count > 0;
        _applyItem.Header = count > 0 ? $"Apply Pending Changes ({count})" : "Apply Pending Changes";
    }

    public void Dispose()
    {
        _settings.SettingChanged -= OnSettingChanged;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
