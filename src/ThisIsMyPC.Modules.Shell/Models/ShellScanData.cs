namespace ThisIsMyPC.Modules.Shell.Models;

public sealed record ShellScanData(
    IReadOnlyList<ContextMenuHandler> ContextMenuHandlers,
    IReadOnlyList<ExplorerPreference> ExplorerPreferences,
    TaskbarSettings Taskbar,
    IReadOnlyList<NotificationSetting> NotificationSettings,
    IReadOnlyList<EnvironmentVariable> UserEnvironmentVariables,
    IReadOnlyList<EnvironmentVariable> SystemEnvironmentVariables);
