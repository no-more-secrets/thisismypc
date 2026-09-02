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
    private readonly IServiceControlService _serviceControlService;
    private readonly IScheduledTaskService _scheduledTaskService;
    private readonly TaskClassificationOverrideStore _classificationOverrides;

    public StartupModule(
        IRegistryService registryService,
        IStartupFolderService startupFolderService,
        IServiceControlService serviceControlService,
        IScheduledTaskService scheduledTaskService,
        TaskClassificationOverrideStore classificationOverrides)
    {
        _registryService = registryService;
        _startupFolderService = startupFolderService;
        _serviceControlService = serviceControlService;
        _scheduledTaskService = scheduledTaskService;
        _classificationOverrides = classificationOverrides;
    }

    public ModuleInfo Info { get; } = new(
        Name: "Startup & Services",
        Icon: "startup",
        Description: "Everything that starts on its own, in Autoruns' categories, with Autoruns-compatible on and off",
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
                var taskScanner = new ScheduledTaskScanner(_scheduledTaskService, _classificationOverrides);
                var scheduledTasks = taskScanner.Scan();

                // Logon/boot-triggered tasks also surface in the Startup section (3-1 AC1)
                var startupScanner = new StartupScanner(
                    _registryService, _startupFolderService,
                    scheduledTaskSource: () => ScheduledTaskScanner.ToStartupEntries(scheduledTasks));
                var serviceScanner = new ServiceScanner(_serviceControlService);
                var startupData = startupScanner.Scan();
                var services = serviceScanner.Scan();

                // The Autoruns view: same tasks and services, plus every other location.
                IReadOnlyList<Models.AutorunEntry> autoruns = [];
                string? autorunsError = null;
                try
                {
                    autoruns = new AutorunsScanner(_registryService, _startupFolderService).Scan(scheduledTasks, services);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    autorunsError = ex.Message;
                }

                return OperationResult<object>.Success(startupData with
                {
                    Services = services,
                    ServicesScanError = serviceScanner.LastScanError,
                    ScheduledTasks = scheduledTasks,
                    ScheduledTasksScanError = taskScanner.LastScanError,
                    Autoruns = autoruns,
                    AutorunsScanError = autorunsError,
                });
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
        return Task.FromResult(change.ValueType switch
        {
            ChangeValueType.Registry_Binary => ApplyBinaryChange(change),
            ChangeValueType.Service_StartType => ApplyServiceStartTypeChange(change),
            ChangeValueType.ScheduledTask_State => ApplyScheduledTaskChange(change),
            ChangeValueType.Autorun_State => ApplyAutorunChange(change),
            _ => OperationResult<bool>.Failure(
                $"Unsupported value type: {change.ValueType}", ErrorCategory.ServiceUnavailable),
        });
    }

    private OperationResult<bool> ApplyAutorunChange(ChangeDescriptor change)
    {
        var enable = string.Equals(change.AfterValue, Changes.AutorunChangeFactory.EnabledValue, StringComparison.OrdinalIgnoreCase);
        if (!enable && !string.Equals(change.AfterValue, Changes.AutorunChangeFactory.DisabledValue, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<bool>.Failure(
                $"Invalid autorun state '{change.AfterValue}' for {change.DisplayName}", ErrorCategory.NotFound);
        }

        var target = AutorunTarget.TryParse(change.SystemLocation);
        if (target is null)
        {
            return OperationResult<bool>.Failure(
                $"Invalid autorun location: {change.SystemLocation}", ErrorCategory.NotFound);
        }

        return new AutorunToggler(_registryService, _startupFolderService, _scheduledTaskService).Apply(target, enable);
    }

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change)
    {
        // Revert contract: callers hand us a Before/After-swapped descriptor.
        return ApplyChangeAsync(change);
    }

    private OperationResult<bool> ApplyScheduledTaskChange(ChangeDescriptor change)
    {
        var enable = string.Equals(change.AfterValue, "Enabled", StringComparison.OrdinalIgnoreCase);
        if (!enable && !string.Equals(change.AfterValue, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<bool>.Failure(
                $"Invalid scheduled-task state '{change.AfterValue}' for {change.DisplayName}", ErrorCategory.NotFound);
        }

        // SystemLocation is the full task path
        return _scheduledTaskService.SetEnabled(change.SystemLocation, enable);
    }

    private OperationResult<bool> ApplyServiceStartTypeChange(ChangeDescriptor change)
    {
        if (!Enum.TryParse<ServiceStartType>(change.AfterValue, out var startType) ||
            !Enum.IsDefined(startType))
        {
            return OperationResult<bool>.Failure(
                $"Invalid service start type '{change.AfterValue}' for {change.DisplayName}", ErrorCategory.NotFound);
        }

        // SystemLocation is the bare service name
        return _serviceControlService.SetStartType(change.SystemLocation, startType);
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
