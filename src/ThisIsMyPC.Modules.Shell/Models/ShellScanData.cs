namespace ThisIsMyPC.Modules.Shell.Models;

/// <param name="ExplorerPatcherSettings">Settings ExplorerPatcher exposes; empty when it is not installed.</param>
/// <param name="ExplorerPatcherInstalled">True when ExplorerPatcher is installed, so its settings take effect.</param>
public sealed record ShellScanData(
    IReadOnlyList<ExplorerPreference> ExplorerPreferences,
    TaskbarSettings Taskbar,
    IReadOnlyList<ExplorerPatcherSetting>? ExplorerPatcherSettings = null,
    bool ExplorerPatcherInstalled = false)
{
    public IReadOnlyList<ExplorerPatcherSetting> ExplorerPatcherSettings { get; init; } =
        ExplorerPatcherSettings ?? [];
}
