using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.Services;

public enum CloseDecision { Terminate, HideToTray }

/// <summary>
/// Close hides the window only when tray mode is enabled. Minimize always uses the
/// taskbar and is left to the window manager.
/// </summary>
public static class WindowBehaviorPolicy
{
    public static CloseDecision DecideClose(bool trayModeEnabled) =>
        trayModeEnabled ? CloseDecision.HideToTray : CloseDecision.Terminate;

    public static CloseDecision DecideClose(ISettingsService settings) =>
        DecideClose(settings.GetAppBool(AppSettingKeys.TrayMode, false));

    public static void NormalizeLegacySettings(ISettingsService settings)
    {
        var closeAction = settings.GetAppBool(AppSettingKeys.TrayMode, false) ? "tray" : "exit";
        if (settings.GetApp(AppSettingKeys.CloseAction, "exit") != closeAction)
            settings.SetApp(AppSettingKeys.CloseAction, closeAction);
        if (settings.GetApp(AppSettingKeys.MinimizeAction, "taskbar") != "taskbar")
            settings.SetApp(AppSettingKeys.MinimizeAction, "taskbar");
    }
}
