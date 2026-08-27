using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Interop.Win32.Registry;

using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.Modules.Shell;

public sealed class ContextMenuModule : IModule
{
    private readonly IRegistryService _registryService;
    private readonly ContextMenuScanner _contextMenuScanner;

    public ContextMenuModule(
        IRegistryService registryService,
        IShellExtensionService shellExtensionService,
        IContextMenuProbe contextMenuProbe)
    {
        _registryService = registryService;
        var staticVerbService = new StaticVerbService(registryService, ShellRegistryPaths.StaticVerbScopePaths);
        var modernPackagedService = new ModernPackagedHandlerService();
        var progIdResolver = new ProgIdResolver(registryService);
        var fileTypeVerbService = new FileTypeVerbService(registryService);
        _contextMenuScanner = new ContextMenuScanner(
            shellExtensionService, contextMenuProbe, staticVerbService, modernPackagedService,
            progIdResolver, fileTypeVerbService);
    }

    public ModuleInfo Info { get; } = new(
        Name: "Context Menus",
        Icon: "context-menu",
        Description: "Manage shell extensions that add items to your right-click menu",
        RequiredCapabilities: [SystemCapability.Registry, SystemCapability.Com],
        Group: ModuleGroup.Core,
        LoadOrder: 2);

    public Task<ModuleAvailability> CheckAvailabilityAsync()
    {
        return Task.FromResult(new ModuleAvailability(IsAvailable: true));
    }

    public async Task<OperationResult<object>> ScanSystemStateAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var handlers = _contextMenuScanner.Scan();
                return OperationResult<object>.Success(handlers);
            }
            catch (Exception ex)
            {
                return OperationResult<object>.Failure(
                    $"Context menu scan failed: {ex.Message}",
                    ErrorCategory.ServiceUnavailable, ex);
            }
        }).ConfigureAwait(false);
    }

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change)
    {
        try
        {
            var result = change.ValueType switch
            {
                ChangeValueType.Registry_String => ApplyStringChange(change),
                _ => OperationResult<bool>.Failure(
                    $"Unsupported value type: {change.ValueType}",
                    ErrorCategory.ServiceUnavailable),
            };

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult<bool>.Failure(
                $"Failed to apply change '{change.DisplayName}': {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex));
        }
    }

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change)
    {
        // The revert contract is "apply the descriptor's AfterValue": both
        // ChangeHistoryService undo and PendingChangesService mid-group rollback hand
        // this a Before/After-swapped descriptor. (The previous write-BeforeValue
        // implementation expected the unswapped descriptor and silently re-applied
        // changes on the history-undo path.)
        return ApplyChangeAsync(change);
    }

    private OperationResult<bool> ApplyStringChange(ChangeDescriptor change)
    {
        var (keyPath, valueName) = ShellRegistryPaths.ParseSystemLocation(change.SystemLocation);

        // AbsentValue signals delete (remove from blocked list = re-enable handler)
        var result = change.AfterValue == ShellRegistryPaths.AbsentValue
            ? _registryService.DeleteValue(keyPath, valueName)
            : _registryService.WriteString(keyPath, valueName, change.AfterValue ?? string.Empty);

        return MakeBestEffortIfHkcrAccessDenied(result, change);
    }

    /// <summary>
    /// HKCR dash-prefix writes are best-effort because the blocked list is the
    /// authoritative disable mechanism. If a dash-prefix write fails with AccessDenied
    /// on a TrustedInstaller-owned HKCR path, return Success — the blocked list change
    /// in the same ChangeGroup handles the actual disable, taking effect after Explorer restart.
    /// Delete operations (orphan cleanup) are NOT best-effort — they must succeed or fail honestly.
    /// </summary>
    private static OperationResult<bool> MakeBestEffortIfHkcrAccessDenied(
        OperationResult<bool> result, ChangeDescriptor change)
    {
        if (result.IsSuccess || result.ErrorCategory != ErrorCategory.AccessDenied)
            return result;

        if (!change.SystemLocation.StartsWith("HKCR\\", StringComparison.OrdinalIgnoreCase))
            return result;

        // Only Disable/Enable (dash-prefix toggles alongside blocked list) get best-effort.
        // Delete (orphan cleanup) and Modify (migration CLSID restore) must fail honestly.
        if (change.Category is not (ChangeCategory.Disable or ChangeCategory.Enable))
            return result;

        System.Diagnostics.Debug.WriteLine(
            $"Best-effort: HKCR dash-prefix write skipped (TrustedInstaller-protected): {change.SystemLocation}");
        return OperationResult<bool>.Success(true);
    }
}
