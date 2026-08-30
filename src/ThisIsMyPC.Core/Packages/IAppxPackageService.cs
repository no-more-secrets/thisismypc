using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Packages;

/// <summary>
/// AppX/MSIX package enumeration, removal, and deprovisioning via the Windows deployment
/// stack. All failures surface as OperationResult; the one exception is caller-requested
/// cancellation, which propagates as OperationCanceledException from the async members.
/// The mutating members honor cancellation only before the deployment starts; a removal
/// or deprovision already in flight always runs to completion so its outcome is known.
/// Package removal deletes the package's files and is NOT locally undoable; undo means
/// reinstalling from the Store or re-provisioning. Deprovisioning only marks the package
/// so it stops auto-installing for new user profiles, and is reversible.
/// </summary>
public interface IAppxPackageService
{
    /// <summary>
    /// All packages registered for the current user, with best-effort provisioned flags
    /// (null flags when the provisioned list is unreadable without elevation).
    /// </summary>
    Task<OperationResult<IReadOnlyList<AppxPackageInfo>>> EnumeratePackagesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>A single package by full name; the before-state for a ChangeDescriptor.</summary>
    Task<OperationResult<AppxPackageInfo>> QueryPackageAsync(
        string packageFullName, CancellationToken cancellationToken = default);

    /// <summary>Removes the package for the current user, or for all users when <paramref name="allUsers"/> is true.</summary>
    Task<OperationResult<bool>> RemovePackageAsync(
        string packageFullName, bool allUsers = false, CancellationToken cancellationToken = default);

    /// <summary>Stops the package family from auto-installing into new user profiles.</summary>
    Task<OperationResult<bool>> DeprovisionPackageAsync(
        string packageFamilyName, CancellationToken cancellationToken = default);
}
