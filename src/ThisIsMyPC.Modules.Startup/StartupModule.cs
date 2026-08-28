using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Services;

namespace ThisIsMyPC.Modules.Startup;

public sealed class StartupModule : IModule
{
    private readonly IRegistryService _registryService;
    private readonly IStartupFolderService _startupFolderService;

    public StartupModule(IRegistryService registryService, IStartupFolderService startupFolderService)
    {
        _registryService = registryService;
        _startupFolderService = startupFolderService;
    }

    public ModuleInfo Info { get; } = new(
        Name: "Startup & Services",
        Icon: "startup",
        Description: "Manage startup entries, Windows services, and scheduled tasks",
        RequiredCapabilities: [SystemCapability.Registry, SystemCapability.Com],
        Group: ModuleGroup.Core,
        LoadOrder: 4);

    public Task<ModuleAvailability> CheckAvailabilityAsync()
    {
        return Task.FromResult(new ModuleAvailability(IsAvailable: true));
    }

    public Task<OperationResult<object>> ScanSystemStateAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var scanner = new StartupScanner(_registryService, _startupFolderService);
                return OperationResult<object>.Success(scanner.Scan());
            }
            catch (Exception ex)
            {
                return OperationResult<object>.Failure(
                    $"Failed to scan startup entries: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
            }
        });
    }

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change)
    {
        // Services (3.3) and scheduled tasks (3.4) add their value types here.
        return Task.FromResult(change.ValueType switch
        {
            ChangeValueType.Registry_Binary => ApplyBinaryChange(change),
            _ => OperationResult<bool>.Failure(
                $"Unsupported value type: {change.ValueType}", ErrorCategory.ServiceUnavailable),
        });
    }

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change)
    {
        // Revert contract: callers hand us a Before/After-swapped descriptor.
        return ApplyChangeAsync(change);
    }

    private OperationResult<bool> ApplyBinaryChange(ChangeDescriptor change)
    {
        var separator = change.SystemLocation.LastIndexOf('\\');
        if (separator <= 0 || separator == change.SystemLocation.Length - 1)
        {
            return OperationResult<bool>.Failure(
                $"Invalid system location: {change.SystemLocation}", ErrorCategory.NotFound);
        }

        var keyPath = change.SystemLocation[..separator];
        var valueName = change.SystemLocation[(separator + 1)..];

        // Empty AfterValue restores "value absent" (e.g. reverting a toggle on an
        // entry that had never been touched by Task Manager or us).
        if (string.IsNullOrEmpty(change.AfterValue))
            return _registryService.DeleteValue(keyPath, valueName);

        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(change.AfterValue);
        }
        catch (FormatException ex)
        {
            return OperationResult<bool>.Failure(
                $"Invalid binary value for {change.DisplayName}", ErrorCategory.NotFound, ex);
        }

        return _registryService.WriteBinary(keyPath, valueName, bytes);
    }
}
