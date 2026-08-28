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
        // Mutations arrive in Story 3.2 (startup entries), 3.3 (services), 3.4 (tasks).
        return Task.FromResult(OperationResult<bool>.Failure(
            $"Unsupported value type: {change.ValueType}", ErrorCategory.ServiceUnavailable));
    }

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change)
    {
        return ApplyChangeAsync(change);
    }
}
