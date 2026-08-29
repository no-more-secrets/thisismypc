using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.Services;

public enum CloseDecision { Terminate, HideToTray, MinimizeToTaskbar }

public enum MinimizeDecision { Taskbar, HideToTray }

/// <summary>
/// Pure window-behavior decisions (9-1). Tray-dependent choices fall back to the
/// non-tray behavior whenever tray mode is off — the app must never hide itself with
/// no way back.
/// </summary>
public static class WindowBehaviorPolicy
{
    public static CloseDecision DecideClose(bool trayModeEnabled, string closeAction) => closeAction switch
    {
        "tray" when trayModeEnabled => CloseDecision.HideToTray,
        "taskbar" => CloseDecision.MinimizeToTaskbar,
        _ => CloseDecision.Terminate,
    };

    public static MinimizeDecision DecideMinimize(bool trayModeEnabled, string minimizeAction) =>
        minimizeAction == "tray" && trayModeEnabled
            ? MinimizeDecision.HideToTray
            : MinimizeDecision.Taskbar;

    public static CloseDecision DecideClose(ISettingsService settings) => DecideClose(
        settings.GetAppBool(AppSettingKeys.TrayMode, false),
        settings.GetApp(AppSettingKeys.CloseAction, "exit"));

    public static MinimizeDecision DecideMinimize(ISettingsService settings) => DecideMinimize(
        settings.GetAppBool(AppSettingKeys.TrayMode, false),
        settings.GetApp(AppSettingKeys.MinimizeAction, "taskbar"));
}
