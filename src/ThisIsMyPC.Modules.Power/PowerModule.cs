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
    private readonly IPolicyRefreshService? _policyRefresh;

    public PowerModule(
        IPowerService powerService, IRegistryService registryService, IPolicyRefreshService? policyRefresh = null,
        TimeSpan? policyWaitUnit = null)
    {
        _policyWaitUnit = policyWaitUnit ?? TimeSpan.FromSeconds(1);
        _policyRefresh = policyRefresh;
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

                return OperationResult<object>.Success(
                    (object)new PowerScanData(
                        plans,
                        scanner.LastScanError,
                        hibernateEnabled,
                        Services.PowerPlanScanner.FindUltimatePerformance(plans)));
            }
            catch (Exception ex)
            {
                return OperationResult<object>.Failure(
                    $"Failed to scan power plans: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
            }
        });
    }

    public async Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change)
    {
        if (change.ValueType == ChangeValueType.PowerPlan_Setting && change.SettingId == PowerPlanChangeFactory.ActivePlanSettingId)
            return await ApplyActivePlanChangeAsync(change).ConfigureAwait(false);

        return change.ValueType switch
        {
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
    /// Switches the active plan. While the Group Policy value that pins the
    /// active plan exists, the power service refuses every switch with error
    /// 1260 and enforces a cached copy of the pin: on 2026-09-02 (pin left by
    /// winutil) the registry named the target plan, machine policy had just
    /// been processed, and the service still refused the target while
    /// accepting a switch to the plan it had cached. So the module tries, in
    /// bounded phases, each thing that could make the service re-read the
    /// key: the pin moved to the target plus a subkey added and removed under
    /// the watched key, then a machine policy refresh, then the pin removed
    /// outright. The first phase after which Windows accepts the switch, or
    /// applies the pinned plan itself, wins; the pin then names the active
    /// plan. When every phase fails the old pin goes back and the failure
    /// lists what was tried, how long it waited, and what stayed active.
    /// Undo hands the module the swapped descriptor, so the same steps run
    /// in reverse.
    /// </summary>
    private async Task<OperationResult<bool>> ApplyActivePlanChangeAsync(ChangeDescriptor change)
    {
        if (!Guid.TryParse(change.AfterValue, out var planGuid))
        {
            return OperationResult<bool>.Failure(
                $"Invalid power plan GUID '{change.AfterValue}' for {change.DisplayName}", ErrorCategory.NotFound);
        }

        const string key = PowerPlanChangeFactory.ActivePlanPolicyKeyPath;
        const string name = PowerPlanChangeFactory.ActivePlanPolicyValueName;
        var pinned = _registryService.ReadString(key, name);
        if (!pinned.IsSuccess || string.IsNullOrWhiteSpace(pinned.Value))
            return _powerService.SetActivePlan(planGuid);

        var target = planGuid.ToString("D");
        var moved = _registryService.WriteString(key, name, target);
        if (!moved.IsSuccess)
        {
            return OperationResult<bool>.Failure(
                $"A Group Policy pins the active power plan and could not be moved: {moved.ErrorMessage}",
                moved.ErrorCategory ?? ErrorCategory.AccessDenied, moved.Exception);
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var tried = new List<string>();

        // Phase 1: the pin names the target; a subkey added and removed under
        // the key is a name change, which a registry watch sees even when a
        // value write did not wake it.
        var nudgeKey = key + "\\" + NudgeSubKeyName;
        var nudged = _registryService.CreateKey(nudgeKey);
        if (nudged.IsSuccess)
            _ = _registryService.DeleteKey(nudgeKey);
        var activated = await WaitForSwitchAsync(planGuid, _policyWaitUnit * 3).ConfigureAwait(false);
        if (activated.IsSuccess)
            return activated;
        if (activated.ErrorCategory != ErrorCategory.AccessDenied)
            return PutPinBack(activated, pinned.Value, key, name, tried, clock);
        tried.Add(nudged.IsSuccess ? "moved the pin to the new plan and nudged the policy key" : "moved the pin to the new plan");

        // Phase 2: machine policy processed again, the way gpupdate does it.
        var refresh = _policyRefresh?.RefreshMachinePolicy();
        activated = await WaitForSwitchAsync(planGuid, _policyWaitUnit * 5).ConfigureAwait(false);
        if (activated.IsSuccess)
            return activated;
        if (activated.ErrorCategory != ErrorCategory.AccessDenied)
            return PutPinBack(activated, pinned.Value, key, name, tried, clock);
        tried.Add(refresh is null ? "no policy refresh available"
            : refresh.IsSuccess ? "refreshed machine policy" : $"policy refresh failed ({refresh.ErrorMessage})");

        // Phase 3: no pin at all; if the service reads the key live this is
        // the moment it stops refusing. The pin comes back on the new plan
        // afterwards so policy and reality agree.
        var lifted = _registryService.DeleteValue(key, name);
        if (lifted.IsSuccess)
        {
            activated = await WaitForSwitchAsync(planGuid, _policyWaitUnit * 6).ConfigureAwait(false);
            if (activated.IsSuccess)
            {
                _ = _registryService.WriteString(key, name, target);
                return activated;
            }
            tried.Add("removed the pin");
        }
        else
        {
            tried.Add($"could not remove the pin ({lifted.ErrorMessage})");
        }

        return PutPinBack(activated, pinned.Value, key, name, tried, clock);
    }

    private OperationResult<bool> PutPinBack(
        OperationResult<bool> activated, string oldPin, string key, string name, List<string> tried, System.Diagnostics.Stopwatch clock)
    {
        _ = _registryService.WriteString(key, name, oldPin);
        var activeName = _powerService.EnumeratePlans() is { IsSuccess: true, Value: { } plans }
            ? plans.FirstOrDefault(p => p.IsActive)?.Name ?? "unknown"
            : "unknown";
        var steps = tried.Count == 0 ? "" : $" The app {string.Join(", then ", tried)};";
        return OperationResult<bool>.Failure(
            $"{activated.ErrorMessage}{steps} after {clock.Elapsed.TotalSeconds:0} seconds '{activeName}' stayed active and the pin was put back. "
            + "A restart applies a pinned plan.",
            activated.ErrorCategory ?? ErrorCategory.AccessDenied, activated.Exception);
    }

    /// <summary>
    /// Asks for the switch until Windows accepts it, applies the pinned plan
    /// itself, fails for a reason other than policy, or the budget runs out.
    /// </summary>
    private async Task<OperationResult<bool>> WaitForSwitchAsync(Guid planGuid, TimeSpan budget)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var interval = TimeSpan.FromTicks(Math.Min(_policyWaitUnit.Ticks / 2, TimeSpan.FromMilliseconds(500).Ticks));
        while (true)
        {
            var activated = _powerService.SetActivePlan(planGuid);
            if (activated.IsSuccess || activated.ErrorCategory != ErrorCategory.AccessDenied)
                return activated;
            if (IsActive(planGuid))
                return OperationResult<bool>.Success(true);
            if (clock.Elapsed >= budget)
                return activated;
            await Task.Delay(interval).ConfigureAwait(false);
        }
    }

    /// <summary>Short-lived subkey under the policy key; a name change there wakes a registry watch.</summary>
    private const string NudgeSubKeyName = "ThisIsMyPC-refresh";

    /// <summary>One unit of waiting for the power service to re-read policy; phases wait 3, 5, and 6 units.</summary>
    private readonly TimeSpan _policyWaitUnit;

    private bool IsActive(Guid planGuid) =>
        _powerService.EnumeratePlans() is { IsSuccess: true, Value: { } plans }
        && plans.Any(p => p.PlanGuid == planGuid && p.IsActive);
}
