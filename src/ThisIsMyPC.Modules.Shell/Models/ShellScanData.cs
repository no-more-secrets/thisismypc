namespace ThisIsMyPC.Modules.Shell.Models;

public sealed record ShellScanData(
    IReadOnlyList<ExplorerPreference> ExplorerPreferences,
    TaskbarSettings Taskbar,
    IReadOnlyList<NotificationSetting> NotificationSettings);
