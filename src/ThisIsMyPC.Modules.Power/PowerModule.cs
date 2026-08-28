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

    public PowerModule(IPowerService powerService)
    {
        _powerService = powerService;
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
            _ => OperationResult<bool>.Failure(
                $"Unsupported change: {change.ValueType}/{change.SettingId}", ErrorCategory.ServiceUnavailable),
        });
    }

    public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change)
    {
        // Revert contract: callers hand us a Before/After-swapped descriptor.
        return ApplyChangeAsync(change);
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
