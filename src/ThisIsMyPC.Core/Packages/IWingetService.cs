using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Packages;

/// <summary>Which winget source a package id belongs to.</summary>
public enum WingetSource
{
    Winget,
    MsStore,
}

/// <summary>
/// A package winget reports as installed. <see cref="PackageId"/> is empty for
/// installs winget could not correlate to a source package (raw ARP rows);
/// <see cref="Name"/> carries the display name for name-based matching.
/// </summary>
public sealed record InstalledWingetPackage(string PackageId, string? Version, string? Name = null);

/// <summary>A package winget reports as having an update available.</summary>
public sealed record UpgradableWingetPackage(
    string PackageId, string Name, string InstalledVersion, string AvailableVersion);

/// <summary>
/// Windows Package Manager (winget) operations. Installs and uninstalls are
/// long-running and one-way; they are staged through the pending-actions queue,
/// never the pending-changes pipeline. Uninstalls run the package's own
/// registered uninstaller.
/// </summary>
public interface IWingetService
{
    /// <summary>Whether winget.exe is available for the current user. Value is the reported version.</summary>
    Task<OperationResult<string>> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Packages currently installed, from the <c>winget list</c> table (local data, fast).</summary>
    Task<OperationResult<IReadOnlyList<InstalledWingetPackage>>> ListInstalledAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult<bool>> InstallAsync(
        string packageId, WingetSource source, CancellationToken cancellationToken = default);

    Task<OperationResult<bool>> UninstallAsync(
        string packageId, WingetSource source, CancellationToken cancellationToken = default);

    /// <summary>Packages with an update available, from the <c>winget upgrade</c> table.</summary>
    Task<OperationResult<IReadOnlyList<UpgradableWingetPackage>>> ListUpgradableAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Updates one package in place; winget resolves the source it was installed from.</summary>
    Task<OperationResult<bool>> UpgradeAsync(
        string packageId, CancellationToken cancellationToken = default);
}
