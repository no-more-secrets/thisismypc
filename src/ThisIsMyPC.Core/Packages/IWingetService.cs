using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Packages;

/// <summary>Which winget source a package id belongs to.</summary>
public enum WingetSource
{
    Winget,
    MsStore,
}

/// <summary>A package winget reports as installed, keyed by its source package id.</summary>
public sealed record InstalledWingetPackage(string PackageId, string? Version);

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

    /// <summary>Package ids currently installed, from <c>winget export</c> (JSON, no table parsing).</summary>
    Task<OperationResult<IReadOnlyList<InstalledWingetPackage>>> ListInstalledAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult<bool>> InstallAsync(
        string packageId, WingetSource source, CancellationToken cancellationToken = default);

    Task<OperationResult<bool>> UninstallAsync(
        string packageId, WingetSource source, CancellationToken cancellationToken = default);
}
