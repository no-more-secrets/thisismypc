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

    public SoftwareModule(IWingetService wingetService)
    {
        _wingetService = wingetService;
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

        var scan = new SoftwareScanData(
            Catalog: SoftwareCatalog.Entries,
            InstalledWingetIds: installedIds,
            InstalledStateKnown: installed.IsSuccess,
            WingetVersion: version.IsSuccess ? version.Value : null);

        return OperationResult<object>.Success(scan);
    }

    public async Task<OperationResult<bool>> ExecuteActionAsync(ActionDescriptor action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var (isInstall, catalogId) = action.ActionId switch
        {
            var id when id.StartsWith(SoftwareActionFactory.InstallPrefix, StringComparison.Ordinal) =>
                (true, id[SoftwareActionFactory.InstallPrefix.Length..]),
            var id when id.StartsWith(SoftwareActionFactory.UninstallPrefix, StringComparison.Ordinal) =>
                (false, id[SoftwareActionFactory.UninstallPrefix.Length..]),
            _ => (false, string.Empty),
        };

        if (catalogId.Length == 0)
        {
            return OperationResult<bool>.Failure(
                $"Unknown action '{action.ActionId}'.", ErrorCategory.NotFound);
        }

        var entry = SoftwareCatalog.Entries.FirstOrDefault(e => e.Id == catalogId);
        if (entry is null)
        {
            return OperationResult<bool>.Failure(
                $"App '{catalogId}' is not in the catalog.", ErrorCategory.NotFound);
        }

        return isInstall
            ? await _wingetService.InstallAsync(entry.WingetId, entry.Source).ConfigureAwait(false)
            : await _wingetService.UninstallAsync(entry.WingetId, entry.Source).ConfigureAwait(false);
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
