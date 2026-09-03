namespace ThisIsMyPC.Modules.Shell.Models;

/// <param name="ExplorerPatcherSettings">Settings ExplorerPatcher exposes; empty when it is not installed.</param>
/// <param name="ExplorerPatcherInstalled">True when ExplorerPatcher is installed, so its settings take effect.</param>
/// <param name="ExplorerPatcherVersion">The installed ExplorerPatcher version, empty when unknown.</param>
/// <param name="ExplorerPatcherCatalogVersion">The version the bundled settings catalog was pinned to.</param>
public sealed record ShellScanData(
    IReadOnlyList<ExplorerPreference> ExplorerPreferences,
    TaskbarSettings Taskbar,
    IReadOnlyList<ExplorerPatcherSetting>? ExplorerPatcherSettings = null,
    bool ExplorerPatcherInstalled = false,
    string ExplorerPatcherVersion = "",
    string ExplorerPatcherCatalogVersion = "")
{
    public IReadOnlyList<ExplorerPatcherSetting> ExplorerPatcherSettings { get; init; } =
        ExplorerPatcherSettings ?? [];

    /// <summary>
    /// True when the installed ExplorerPatcher is not the release the catalog
    /// was built from. The rows still work; a value could have moved, so the
    /// page says so instead of pretending otherwise.
    /// </summary>
    public bool ExplorerPatcherVersionDiffers =>
        ExplorerPatcherInstalled
        && ExplorerPatcherVersion.Length > 0
        && ExplorerPatcherCatalogVersion.Length > 0
        && !string.Equals(ExplorerPatcherVersion, ExplorerPatcherCatalogVersion, StringComparison.OrdinalIgnoreCase);
}
