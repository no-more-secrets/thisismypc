using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Power.Changes;
using ThisIsMyPC.Modules.Power.Models;
using ThisIsMyPC.Modules.Power.Services;

namespace ThisIsMyPC.Modules.Power;

public sealed class PowerModule : IActionModule
{
    /// <summary>
    /// Plan deletion is one-way: a deleted plan's custom settings cannot be
    /// restored, so it runs through the pending-actions queue. Any plan may be
    /// deleted except the active one (debloating the vendor plan zoo is the
    /// point); already gone counts as done.
    /// </summary>
    public async Task<OperationResult<bool>> ExecuteActionAsync(ActionDescriptor action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!action.ActionId.StartsWith(Actions.PowerActionFactory.DeletePlanPrefix, StringComparison.Ordinal)
            || !Guid.TryParse(action.ActionId[Actions.PowerActionFactory.DeletePlanPrefix.Length..], out var planGuid))
        {
            return OperationResult<bool>.Failure(
                $"Unknown action '{action.ActionId}'.", ErrorCategory.NotFound);
        }

        return await Task.Run(() =>
        {
            var enumerated = _powerService.EnumeratePlans();
            if (!enumerated.IsSuccess)
            {
                return OperationResult<bool>.Failure(
                    enumerated.ErrorMessage ?? "Could not enumerate power plans.",
                    enumerated.ErrorCategory ?? ErrorCategory.ServiceUnavailable);
            }

            var plan = enumerated.Value!.FirstOrDefault(p => p.PlanGuid == planGuid);
            if (plan is null)
                return OperationResult<bool>.Success(true);

            if (plan.IsActive)
            {
                return OperationResult<bool>.Failure(
                    $"'{plan.Name}' is the active plan. Switch to another plan first.",
                    ErrorCategory.AccessDenied);
            }

            return _powerService.DeleteScheme(planGuid);
        }).ConfigureAwait(false);
    }
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

                var hibernateRead = _registryService.ReadDWord(
                    PowerPlanChangeFactory.ModernStandbyKeyPath,
                    PowerPlanChangeFactory.HibernateValueName);
                bool? hibernateEnabled = hibernateRead.IsSuccess ? hibernateRead.Value != 0 : null;

                var pinRead = _registryService.ReadString(
                    PowerPlanChangeFactory.ActivePlanPolicyKeyPath, PowerPlanChangeFactory.ActivePlanPolicyValueName);
                Guid? pinnedPlan = pinRead.IsSuccess && Guid.TryParse(pinRead.Value, out var pinGuid) ? pinGuid : null;
                var locked = _powerService.IsActivePlanLockedByPolicy();

                // While locked, the plan Windows will activate at startup is the
                // pin, or the startup scheme once the pin is gone; it matters only
                // when it differs from what is active now.
                Guid? afterRestart = null;
                if (locked)
                {
                    var startupRead = _registryService.ReadString(
                        PowerPlanChangeFactory.StartupActiveSchemeKeyPath, PowerPlanChangeFactory.StartupActiveSchemeValueName);
                    var startup = pinnedPlan
                        ?? (startupRead.IsSuccess && Guid.TryParse(startupRead.Value, out var startupGuid) ? startupGuid : null);
                    var active = plans.FirstOrDefault(p => p.IsActive)?.PlanGuid;
                    if (startup is { } next && next != active)
                        afterRestart = next;
                }

                return OperationResult<object>.Success(
                    (object)new PowerScanData(
                        plans,
                        scanner.LastScanError,
                        hibernateEnabled,
                        Services.PowerPlanScanner.FindUltimatePerformance(plans),
                        pinnedPlan,
                        locked,
                        afterRestart));
            }
            catch (Exception ex)
            {
                return OperationResult<object>.Failure(
                    $"Failed to scan power plans: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
            }
        });
    }

    public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change) => Task.FromResult(ApplyChange(change));

    private OperationResult<bool> ApplyChange(ChangeDescriptor change)
    {
        return change.ValueType switch
        {
            ChangeValueType.PowerPlan_Setting when change.SettingId == PowerPlanChangeFactory.ActivePlanSettingId
                => ApplyActivePlanChange(change),
            ChangeValueType.PowerPlan_Setting when change.SettingId == PowerPlanChangeFactory.ActivePlanPolicyPinSettingId
                => ApplyPolicyPinChange(change),
            ChangeValueType.PowerPlan_Setting when change.SettingId == PowerPlanChangeFactory.HibernateSettingId
                => _powerService.SetHibernateEnabled(change.AfterValue == "1"),
            ChangeValueType.PowerPlan_Setting when change.SettingId == PowerPlanChangeFactory.UltimatePerformanceSettingId
                => ApplyUltimatePerformanceChange(change),
            ChangeValueType.PowerPlan_Setting when change.SettingId.StartsWith(
                PowerPlanChangeFactory.CreatePlanPrefix, StringComparison.Ordinal)
                => ApplyCreatePlanChange(change),
            ChangeValueType.PowerPlan_Setting when change.SettingId.StartsWith(
                PowerPlanChangeFactory.AddStockPlanPrefix, StringComparison.Ordinal)
                => ApplyStockPlanRestore(change),
            ChangeValueType.PowerPlan_Setting when change.SettingId.StartsWith(
                PowerPlanChangeFactory.SettingIdPrefix, StringComparison.Ordinal)
                => ApplySettingChange(change),
            ChangeValueType.Registry_DWord => ApplyRegistryDWordChange(change),
            _ => OperationResult<bool>.Failure(
                $"Unsupported change: {change.ValueType}/{change.SettingId}", ErrorCategory.ServiceUnavailable),
        };
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

    private OperationResult<bool> ApplyUltimatePerformanceChange(ChangeDescriptor change)
    {
        var scanner = new PowerPlanScanner(_powerService);
        var plans = scanner.Scan();

        if (change.AfterValue == "1")
        {
            // Idempotent: history Redo/Restore replays this unconditionally,
            // and a second copy of the plan helps nobody.
            if (PowerPlanScanner.FindUltimatePerformance(plans) is not null)
                return OperationResult<bool>.Success(true);

            var duplicated = _powerService.DuplicateScheme(PowerPlanChangeFactory.UltimatePerformanceSourceGuid);
            if (!duplicated.IsSuccess)
            {
                return OperationResult<bool>.Failure(
                    duplicated.ErrorMessage!, duplicated.ErrorCategory!.Value, duplicated.Exception);
            }

            // The marker is how scan and removal find our copy across locales.
            // An unmarked duplicate would be an orphan the UI can never remove,
            // so a failed text write rolls the install back.
            var marked = _powerService.WriteSchemeText(
                duplicated.Value, "Ultimate Performance", PowerPlanChangeFactory.UltimatePerformanceMarker);
            if (!marked.IsSuccess)
            {
                _ = _powerService.DeleteScheme(duplicated.Value);
                return marked;
            }

            return OperationResult<bool>.Success(true);
        }

        // Deletion is destructive and undo can only recreate a factory-settings
        // copy, so only the plan we created (marker description) may be deleted.
        var target = PowerPlanScanner.FindMarkedUltimatePerformance(plans);
        if (target is null)
        {
            if (PowerPlanScanner.FindUltimatePerformance(plans) is not null)
            {
                return OperationResult<bool>.Failure(
                    "This Ultimate Performance plan was not created by ThisIsMyPC. Use its Delete button in the plan list instead.",
                    ErrorCategory.AccessDenied);
            }

            // Already gone; removal is idempotent.
            return OperationResult<bool>.Success(true);
        }

        if (target.IsActive)
        {
            return OperationResult<bool>.Failure(
                "The Ultimate Performance plan is the active plan. Switch to another plan first.",
                ErrorCategory.ServiceUnavailable);
        }

        return _powerService.DeleteScheme(target.PlanGuid);
    }

    /// <summary>
    /// AfterValue "1": duplicate the source plan and name it; a plan of that
    /// name we already made counts as done. AfterValue "0" (undo): delete the
    /// plan we made, never a plan of the same name someone else made, and
    /// never the active plan.
    /// </summary>
    private OperationResult<bool> ApplyCreatePlanChange(ChangeDescriptor change)
    {
        var name = change.SettingId[PowerPlanChangeFactory.CreatePlanPrefix.Length..];
        var scanner = new PowerPlanScanner(_powerService);
        var plans = scanner.Scan();
        var existing = PowerPlanScanner.FindCreatedPlan(plans, name);

        if (change.AfterValue == "1")
        {
            if (existing is not null)
                return OperationResult<bool>.Success(true);

            if (!PowerPlanChangeFactory.TryParseSourceGuid(change.SystemLocation, out var sourceGuid))
            {
                return OperationResult<bool>.Failure(
                    $"No source plan in '{change.SystemLocation}'.", ErrorCategory.NotFound);
            }

            var duplicated = _powerService.DuplicateScheme(sourceGuid);
            if (!duplicated.IsSuccess)
            {
                return OperationResult<bool>.Failure(
                    duplicated.ErrorMessage!, duplicated.ErrorCategory!.Value, duplicated.Exception);
            }

            // The name and marker are how undo finds the copy; an unnamed
            // duplicate would be an orphan, so a failed text write rolls back.
            var named = _powerService.WriteSchemeText(duplicated.Value, name, PowerPlanChangeFactory.CreatedPlanMarker);
            if (!named.IsSuccess)
            {
                _ = _powerService.DeleteScheme(duplicated.Value);
                return named;
            }

            return OperationResult<bool>.Success(true);
        }

        if (existing is null)
            return OperationResult<bool>.Success(true);

        if (existing.IsActive)
        {
            return OperationResult<bool>.Failure(
                $"'{name}' is the active plan. Switch to another plan first.", ErrorCategory.ServiceUnavailable);
        }

        return _powerService.DeleteScheme(existing.PlanGuid);
    }

    /// <summary>
    /// AfterValue "1": recreate the stock plan under its own GUID from
    /// Windows' default store (PowerRestoreIndividualDefaultPowerScheme;
    /// PowerDuplicateScheme cannot read a deleted plan); present already
    /// counts as done, which also keeps the restore call from resetting a
    /// live plan's settings. AfterValue "0" (undo): delete it again, never
    /// while active.
    /// </summary>
    private OperationResult<bool> ApplyStockPlanRestore(ChangeDescriptor change)
    {
        if (!Guid.TryParse(change.SettingId[PowerPlanChangeFactory.AddStockPlanPrefix.Length..], out var planGuid))
        {
            return OperationResult<bool>.Failure(
                $"No plan GUID in '{change.SettingId}'.", ErrorCategory.NotFound);
        }

        var plans = new PowerPlanScanner(_powerService).Scan();
        var existing = plans.FirstOrDefault(p => p.PlanGuid == planGuid);

        if (change.AfterValue == "1")
        {
            if (existing is not null)
                return OperationResult<bool>.Success(true);
            return _powerService.RestoreDefaultScheme(planGuid);
        }

        if (existing is null)
            return OperationResult<bool>.Success(true);

        if (existing.IsActive)
        {
            return OperationResult<bool>.Failure(
                $"'{existing.Name}' is the active plan. Switch to another plan first.", ErrorCategory.ServiceUnavailable);
        }

        return _powerService.DeleteScheme(planGuid);
    }

    /// <summary>
    /// Switches the active plan. While a Group Policy pin is in force the
    /// power service refuses every switch with error 1260 until the next
    /// restart, and it enforces the copy it read at startup: on 2026-09-02
    /// (pin left by winutil) the registry named the target, machine policy
    /// had just been processed, and the service still refused the target
    /// while accepting the plan it had cached; moving the pin, a key nudge,
    /// a policy refresh, and removing the pin all changed nothing within 14
    /// seconds. So a refused switch is recorded where the service reads at
    /// startup: the pin moves to the target when one exists, otherwise the
    /// startup active scheme is set. The change carries a restart
    /// requirement. Undo hands the module the swapped descriptor, so the
    /// same steps run in reverse.
    /// </summary>
    private OperationResult<bool> ApplyActivePlanChange(ChangeDescriptor change)
    {
        if (!Guid.TryParse(change.AfterValue, out var planGuid))
        {
            return OperationResult<bool>.Failure(
                $"Invalid power plan GUID '{change.AfterValue}' for {change.DisplayName}", ErrorCategory.NotFound);
        }

        const string key = PowerPlanChangeFactory.ActivePlanPolicyKeyPath;
        const string name = PowerPlanChangeFactory.ActivePlanPolicyValueName;
        var target = planGuid.ToString("D");
        var pinned = _registryService.ReadString(key, name);
        var hasPin = pinned.IsSuccess && !string.IsNullOrWhiteSpace(pinned.Value);

        var switched = _powerService.SetActivePlan(planGuid);
        if (switched.IsSuccess)
        {
            // Policy keeps agreeing with what is active.
            if (hasPin && !string.Equals(pinned.Value, target, StringComparison.OrdinalIgnoreCase))
                _ = _registryService.WriteString(key, name, target);
            return switched;
        }
        if (switched.ErrorCategory != ErrorCategory.ProtectedByPolicy)
            return switched;

        if (hasPin)
        {
            var moved = _registryService.WriteString(key, name, target);
            return moved.IsSuccess
                ? OperationResult<bool>.Success(true)
                : OperationResult<bool>.Failure(
                    $"{switched.ErrorMessage} The pin could not be moved to the new plan: {moved.ErrorMessage}",
                    ErrorCategory.ProtectedByPolicy, moved.Exception);
        }

        var startup = _registryService.WriteString(
            PowerPlanChangeFactory.StartupActiveSchemeKeyPath, PowerPlanChangeFactory.StartupActiveSchemeValueName, target);
        return startup.IsSuccess
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure(
                $"{switched.ErrorMessage} The startup plan could not be set either: {startup.ErrorMessage}",
                ErrorCategory.ProtectedByPolicy, startup.Exception);
    }

    /// <summary>An empty AfterValue removes the pin; a GUID writes it. The swapped descriptor undoes either.</summary>
    private OperationResult<bool> ApplyPolicyPinChange(ChangeDescriptor change) =>
        string.IsNullOrEmpty(change.AfterValue)
            ? _registryService.DeleteValue(PowerPlanChangeFactory.ActivePlanPolicyKeyPath, PowerPlanChangeFactory.ActivePlanPolicyValueName)
            : _registryService.WriteString(PowerPlanChangeFactory.ActivePlanPolicyKeyPath, PowerPlanChangeFactory.ActivePlanPolicyValueName, change.AfterValue);
}
