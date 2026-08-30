namespace ThisIsMyPC.Modules.Software.Models;

/// <summary>
/// Scan result for the Software module: the full catalog plus the winget ids
/// currently installed. <see cref="InstalledWingetIds"/> is empty (not null)
/// when installed-state detection failed; <see cref="InstalledStateKnown"/>
/// distinguishes "nothing installed" from "detection unavailable".
/// </summary>
public sealed record SoftwareScanData(
    IReadOnlyList<SoftwareCatalogEntry> Catalog,
    IReadOnlySet<string> InstalledWingetIds,
    bool InstalledStateKnown,
    string? WingetVersion);
