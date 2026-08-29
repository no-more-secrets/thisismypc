namespace ThisIsMyPC.Core.Settings;

/// <summary>
/// Known application-level setting keys and their defaults. Values are string-typed
/// for human-readable JSON and forward compatibility ("1"/"0" for booleans).
/// </summary>
public static class AppSettingKeys
{
    public const string Theme = "theme";                       // "dark" | "light"
    public const string CloseAction = "closeAction";           // "exit" | "tray"
    public const string MinimizeAction = "minimizeAction";     // "taskbar" | "tray"
    public const string DyslexiaFont = "dyslexiaFont";         // bool
    public const string TrayMode = "trayMode";                 // bool (Epic 9 behavior)
    public const string AutoStart = "autoStart";               // bool (Epic 9 behavior)
    public const string Notifications = "notifications";       // bool (Epic 9 behavior)
    public const string UpdateCheck = "updateCheck";           // bool (7-3; opt-out default on)

    public static IReadOnlyDictionary<string, string> Defaults { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Theme] = "dark",
            [CloseAction] = "exit",
            [MinimizeAction] = "taskbar",
            [DyslexiaFont] = "0",
            [TrayMode] = "0",
            [AutoStart] = "0",
            [Notifications] = "1",
            [UpdateCheck] = "1",
        };
}
