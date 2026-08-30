using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Modules.Software.Actions;
using ThisIsMyPC.Modules.Software.Models;
using ThisIsMyPC.Modules.Software.Services;

namespace ThisIsMyPC.Modules.Software;

/// <summary>
/// Software installation engine (Epic 24). All mutations are one-way actions
/// through the pending-actions queue — installs and uninstalls have no
/// before-state, so nothing here touches the pending-changes pipeline.
/// </summary>
public sealed class SoftwareModule : IActionModule
{
    public const string ModuleName = "Software";

    private readonly IWingetService _wingetService;
    private readonly IAppxPackageService _appxPackageService;

    public SoftwareModule(IWingetService wingetService, IAppxPackageService appxPackageService)
    {
        _wingetService = wingetService;
        _appxPackageService = appxPackageService;
    }

    public ModuleInfo Info { get; } = new(
        Name: ModuleName,
        Icon: "software",
        Description: "Install and uninstall apps from a curated catalog through the Windows Package Manager",
        RequiredCapabilities: [SystemCapability.NativeApi],
        Group: ModuleGroup.System,
        LoadOrder: 4);

    public async Task<ModuleAvailability> CheckAvailabilityAsync()
    {
        var version = await _wingetService.GetVersionAsync().ConfigureAwait(false);
        return version.IsSuccess
            ? new ModuleAvailability(IsAvailable: true)
            : new ModuleAvailability(
                IsAvailable: false,
                Reason: version.ErrorMessage,
                RemediationHint: "Install 'App Installer' from the Microsoft Store to get winget.");
    }

    public async Task<OperationResult<object>> ScanSystemStateAsync()
    {
        var version = await _wingetService.GetVersionAsync().ConfigureAwait(false);

        // Installed-state detection is best-effort: a failed export still leaves
        // the catalog browsable, it just cannot mark what is already installed.
        var installed = await _wingetService.ListInstalledAsync().ConfigureAwait(false);
        var installedIds = installed.IsSuccess
            ? installed.Value!.Select(p => p.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        var appxPackages = await _appxPackageService.EnumeratePackagesAsync().ConfigureAwait(false);
        var presentAppxIds = appxPackages.IsSuccess
            ? WindowsAppsCatalog.Entries
                .Where(entry => appxPackages.Value!.Any(p => MatchesPackageId(p, entry.PackageId)))
                .Select(entry => entry.PackageId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        var scan = new SoftwareScanData(
            Catalog: SoftwareCatalog.Entries,
            InstalledWingetIds: installedIds,
            InstalledStateKnown: installed.IsSuccess,
            WingetVersion: version.IsSuccess ? version.Value : null,
            WindowsApps: WindowsAppsCatalog.Entries,
            PresentAppxPackageIds: presentAppxIds,
            AppxStateKnown: appxPackages.IsSuccess);

        return OperationResult<object>.Success(scan);
    }

    /// <summary>Catalog PackageId is the AppX package name — the family name minus the publisher suffix.</summary>
    private static bool MatchesPackageId(AppxPackageInfo package, string packageId) =>
        package.PackageFamilyName.StartsWith(packageId + "_", StringComparison.OrdinalIgnoreCase);

    public async Task<OperationResult<bool>> ExecuteActionAsync(ActionDescriptor action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (TryStripPrefix(action.ActionId, SoftwareActionFactory.InstallPrefix, out var catalogId))
            return await RunCatalogOperationAsync(catalogId, install: true).ConfigureAwait(false);

        if (TryStripPrefix(action.ActionId, SoftwareActionFactory.UninstallPrefix, out catalogId))
            return await RunCatalogOperationAsync(catalogId, install: false).ConfigureAwait(false);

        if (TryStripPrefix(action.ActionId, SoftwareActionFactory.AppxRemovePrefix, out var appId))
            return await RemoveWindowsAppAsync(appId).ConfigureAwait(false);

        if (TryStripPrefix(action.ActionId, SoftwareActionFactory.AppxReinstallPrefix, out appId))
            return await ReinstallWindowsAppAsync(appId).ConfigureAwait(false);

        return OperationResult<bool>.Failure(
            $"Unknown action '{action.ActionId}'.", ErrorCategory.NotFound);
    }

    private static bool TryStripPrefix(string actionId, string prefix, out string rest)
    {
        if (actionId.StartsWith(prefix, StringComparison.Ordinal) && actionId.Length > prefix.Length)
        {
            rest = actionId[prefix.Length..];
            return true;
        }

        rest = string.Empty;
        return false;
    }

    private async Task<OperationResult<bool>> RunCatalogOperationAsync(string catalogId, bool install)
    {
        var entry = SoftwareCatalog.Entries.FirstOrDefault(e => e.Id == catalogId);
        if (entry is null)
        {
            return OperationResult<bool>.Failure(
                $"App '{catalogId}' is not in the catalog.", ErrorCategory.NotFound);
        }

        return install
            ? await _wingetService.InstallAsync(entry.WingetId, entry.Source).ConfigureAwait(false)
            : await _wingetService.UninstallAsync(entry.WingetId, entry.Source).ConfigureAwait(false);
    }

    private async Task<OperationResult<bool>> RemoveWindowsAppAsync(string appId)
    {
        var entry = WindowsAppsCatalog.Entries.FirstOrDefault(e => e.Id == appId);
        if (entry is null)
        {
            return OperationResult<bool>.Failure(
                $"Windows app '{appId}' is not in the catalog.", ErrorCategory.NotFound);
        }

        var packages = await _appxPackageService.EnumeratePackagesAsync().ConfigureAwait(false);
        if (!packages.IsSuccess)
        {
            return OperationResult<bool>.Failure(
                packages.ErrorMessage ?? "Could not enumerate installed packages.",
                packages.ErrorCategory ?? ErrorCategory.ServiceUnavailable);
        }

        var matches = packages.Value!
            .Where(p => !p.IsFramework && MatchesPackageId(p, entry.PackageId))
            .ToList();

        // Already gone counts as done — the queue is idempotent about state
        // that changed between staging and Apply.
        foreach (var package in matches)
        {
            var removal = await _appxPackageService
                .RemovePackageAsync(package.PackageFullName, allUsers: true).ConfigureAwait(false);
            if (!removal.IsSuccess)
                return removal;

            // null means the provisioned list was unreadable — attempt anyway so
            // the promised "stops auto-install for new profiles" holds, but only
            // a known-provisioned package turns a deprovision error into failure.
            if (package.IsProvisioned != false)
            {
                var deprovision = await _appxPackageService
                    .DeprovisionPackageAsync(package.PackageFamilyName).ConfigureAwait(false);
                if (!deprovision.IsSuccess && package.IsProvisioned == true)
                    return deprovision;
            }
        }

        return OperationResult<bool>.Success(true);
    }

    private async Task<OperationResult<bool>> ReinstallWindowsAppAsync(string appId)
    {
        var entry = WindowsAppsCatalog.Entries.FirstOrDefault(e => e.Id == appId);
        if (entry is null)
        {
            return OperationResult<bool>.Failure(
                $"Windows app '{appId}' is not in the catalog.", ErrorCategory.NotFound);
        }

        if (!entry.CanReinstall)
        {
            return OperationResult<bool>.Failure(
                $"{entry.Name} has no Store listing to reinstall from.", ErrorCategory.NotFound);
        }

        return await _wingetService
            .InstallAsync(entry.StoreId, WingetSource.MsStore).ConfigureAwait(false);
    }

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change) =>
        Task.FromResult(OperationResult<bool>.Failure(
            "The Software module has no reversible changes; use the actions queue.",
            ErrorCategory.NotFound));

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change) =>
        Task.FromResult(OperationResult<bool>.Failure(
            "The Software module has no reversible changes; use the actions queue.",
            ErrorCategory.NotFound));
}
