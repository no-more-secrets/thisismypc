using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Power.Changes;
using ThisIsMyPC.Modules.Power.Models;
using ThisIsMyPC.Modules.Power.Services;

namespace ThisIsMyPC.Modules.Power;

public sealed class PowerModule : IModule
{
    private readonly IPowerService _powerService;
    private readonly IRegistryService _registryService;

    public PowerModule(IPowerService powerService, IRegistryService registryService)
    {
        _powerService = powerService;
        _registryService = registryService;
    }

    public ModuleInfo Info { get; } = new(
        Name: "Power Plans",
        Icon: "power",
        Description: "Discover, switch, and adjust power plan settings",
        RequiredCapabilities: [SystemCapability.NativeApi],
        Group: ModuleGroup.Core,
        LoadOrder: 3);

    public Task<ModuleAvailability> CheckAvailabilityAsync()
    {
        // powrprof.dll is always present on Windows
        return Task.FromResult(new ModuleAvailability(IsAvailable: true));
    }

    public Task<OperationResult<object>> ScanSystemStateAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var scanner = new PowerPlanScanner(_powerService);
                var plans = scanner.Scan();
                return OperationResult<object>.Success(
                    (object)new PowerScanData(plans, scanner.LastScanError));
            }
            catch (Exception ex)
            {
                return OperationResult<object>.Failure(
                    $"Failed to scan power plans: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
            }
        });
    }

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change)
    {
        return Task.FromResult(change.ValueType switch
        {
            ChangeValueType.PowerPlan_Setting when change.SettingId == PowerPlanChangeFactory.ActivePlanSettingId
                => ApplyActivePlanChange(change),
            ChangeValueType.PowerPlan_Setting when change.SettingId.StartsWith(
                PowerPlanChangeFactory.SettingIdPrefix, StringComparison.Ordinal)
                => ApplySettingChange(change),
            ChangeValueType.Registry_DWord => ApplyRegistryDWordChange(change),
            _ => OperationResult<bool>.Failure(
                $"Unsupported change: {change.ValueType}/{change.SettingId}", ErrorCategory.ServiceUnavailable),
        });
    }

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change)
    {
        // Revert contract: callers hand us a Before/After-swapped descriptor.
        return ApplyChangeAsync(change);
    }

    /// <summary>SystemLocation is "{planGuid}/{subgroupGuid}/{settingGuid}/{AC|DC}".</summary>
    private OperationResult<bool> ApplySettingChange(ChangeDescriptor change)
    {
        var parts = change.SystemLocation.Split('/');
        if (parts.Length != 4 ||
            !Guid.TryParse(parts[0], out var planGuid) ||
            !Guid.TryParse(parts[1], out var subgroupGuid) ||
            !Guid.TryParse(parts[2], out var settingGuid) ||
            parts[3] is not ("AC" or "DC") ||
            !uint.TryParse(change.AfterValue, out var valueIndex))
        {
            return OperationResult<bool>.Failure(
                $"Invalid power setting change for {change.DisplayName}: '{change.SystemLocation}' = '{change.AfterValue}'",
                ErrorCategory.NotFound);
        }

        return _powerService.WriteSettingIndex(planGuid, subgroupGuid, settingGuid, ac: parts[3] == "AC", valueIndex);
    }

    /// <summary>Empty AfterValue restores "value absent" (e.g. reverting PlatformAoAcOverride to the Windows default).</summary>
    private OperationResult<bool> ApplyRegistryDWordChange(ChangeDescriptor change)
    {
        var separator = change.SystemLocation.LastIndexOf('\\');
        if (separator <= 0 || separator == change.SystemLocation.Length - 1)
        {
            return OperationResult<bool>.Failure(
                $"Invalid system location: {change.SystemLocation}", ErrorCategory.NotFound);
        }

        var keyPath = change.SystemLocation[..separator];
        var valueName = change.SystemLocation[(separator + 1)..];

        if (string.IsNullOrEmpty(change.AfterValue))
            return _registryService.DeleteValue(keyPath, valueName);

        if (!int.TryParse(change.AfterValue, out var value))
        {
            return OperationResult<bool>.Failure(
                $"Invalid DWORD value '{change.AfterValue}' for {change.DisplayName}", ErrorCategory.NotFound);
        }

        return _registryService.WriteDWord(keyPath, valueName, value);
    }

    private OperationResult<bool> ApplyActivePlanChange(ChangeDescriptor change)
    {
        if (!Guid.TryParse(change.AfterValue, out var planGuid))
        {
            return OperationResult<bool>.Failure(
                $"Invalid power plan GUID '{change.AfterValue}' for {change.DisplayName}", ErrorCategory.NotFound);
        }

        return _powerService.SetActivePlan(planGuid);
    }
}
