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
        _contextMenuScanner = new ContextMenuScanner(
            shellExtensionService, contextMenuProbe, staticVerbService, modernPackagedService);
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
        try
        {
            var result = change.ValueType switch
            {
                ChangeValueType.Registry_String => RevertStringChange(change),
                _ => OperationResult<bool>.Failure(
                    $"Unsupported value type: {change.ValueType}",
                    ErrorCategory.ServiceUnavailable),
            };

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult<bool>.Failure(
                $"Failed to revert change '{change.DisplayName}': {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex));
        }
    }

    private OperationResult<bool> ApplyStringChange(ChangeDescriptor change)
    {
        var (keyPath, valueName) = ShellRegistryPaths.ParseSystemLocation(change.SystemLocation);

        // AbsentValue signals delete (remove from blocked list = re-enable handler)
        return change.AfterValue == ShellRegistryPaths.AbsentValue
            ? _registryService.DeleteValue(keyPath, valueName)
            : _registryService.WriteString(keyPath, valueName, change.AfterValue ?? string.Empty);
    }

    private OperationResult<bool> RevertStringChange(ChangeDescriptor change)
    {
        var (keyPath, valueName) = ShellRegistryPaths.ParseSystemLocation(change.SystemLocation);

        // Write BeforeValue to restore original state (opposite of ApplyStringChange)
        return change.BeforeValue == ShellRegistryPaths.AbsentValue
            ? _registryService.DeleteValue(keyPath, valueName)
            : _registryService.WriteString(keyPath, valueName, change.BeforeValue ?? string.Empty);
    }
}
