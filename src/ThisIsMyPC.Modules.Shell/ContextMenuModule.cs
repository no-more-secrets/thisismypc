using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Models;
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
        _contextMenuScanner = new ContextMenuScanner(shellExtensionService, contextMenuProbe);
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
        return ApplyChangeAsync(change);
    }

    private OperationResult<bool> ApplyStringChange(ChangeDescriptor change)
    {
        var (keyPath, valueName) = ShellRegistryPaths.ParseSystemLocation(change.SystemLocation);
        return _registryService.WriteString(keyPath, valueName, change.AfterValue ?? string.Empty);
    }
}
