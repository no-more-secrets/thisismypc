using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Production enforcement orchestrator: companion services are disabled around the
/// primary mutation (supplied as a delegate; the executor never calls modules directly)
/// and restored on failure. Companion scheduled tasks are disabled/re-enabled the same
/// way when a task service is supplied, and GPCache entries are cleared before the
/// primary mutation when a registry service is supplied (Windows rebuilds the cache
/// from the policy hive; see the 26-8 derived-state rule). The ACL enforcement
/// dimension is not yet supported and fails up front rather than silently partially
/// enforcing. No exceptions escape except OperationCanceledException on caller
/// cancellation.
/// </summary>
public sealed class EnforcementExecutor : IEnforcementExecutor
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.App.Services.EnforcementExecutor");
    private static readonly TimeSpan ServiceStopTimeout = TimeSpan.FromSeconds(30);

    private readonly IServiceControlService _serviceControl;
    private readonly IScheduledTaskService? _scheduledTasks;
    private readonly IRegistryService? _registry;

    public EnforcementExecutor(
        IServiceControlService serviceControl,
        IScheduledTaskService? scheduledTasks = null,
        IRegistryService? registry = null)
    {
        ArgumentNullException.ThrowIfNull(serviceControl);
        _serviceControl = serviceControl;
        _scheduledTasks = scheduledTasks;
        _registry = registry;
    }

    public async Task<EnforcementResult> ExecuteAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyPrimary,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(applyPrimary);

        try
        {
            var enforcement = change.Enforcement;
            if (enforcement is null)
            {
                // PendingChangesService only routes non-null enforcement here, but a direct
                // caller gets plain delegate execution rather than a throw.
                return await RunPrimaryOnlyAsync(change, applyPrimary).ConfigureAwait(false);
            }

            if (Gate(change, enforcement) is { } gateFailure)
                return gateFailure;

            // Directional companions: a restore-direction change re-enables its
            // companions instead of disabling them; same sequence RevertAsync runs.
            if (enforcement.RestoresCompanions)
                return await RunRestoreShapedAsync(change, enforcement, applyPrimary).ConfigureAwait(false);

            return await RunConfigureShapedAsync(change, enforcement, applyPrimary, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new EnforcementResult
            {
                IsSuccess = false,
                ErrorMessage = $"Unexpected enforcement error for '{change.SettingId}': {ex.Message}",
                ErrorCategory = ErrorCategory.ServiceUnavailable,
            };
        }
    }

    /// <summary>
    /// Configure-shaped sequence: companions disabled (with rollback), GPCache cleared,
    /// then the primary mutation. Used by ExecuteAsync for configure changes and by
    /// RevertAsync for changes flagged <see cref="SettingEnforcement.RestoresCompanions"/>
    /// (undoing a restore re-hardens).
    /// </summary>
    private async Task<EnforcementResult> RunConfigureShapedAsync(
        ChangeDescriptor change,
        SettingEnforcement enforcement,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> primaryAction,
        CancellationToken cancellationToken)
    {
        {
            var steps = new List<EnforcementStepResult>();
            var disabled = new List<(string Name, ServiceStatusInfo Before)>();
            var disabledTasks = new List<(string Path, bool WasEnabled)>();

            try
            {
                foreach (var serviceName in enforcement.CompanionServices ?? [])
                {
                    var disableResult = await DisableCompanionAsync(serviceName, cancellationToken).ConfigureAwait(false);
                    if (!disableResult.Result.IsSuccess)
                    {
                        steps.Add(Step(EnforcementStepType.DisableService, serviceName, disableResult.Result));
                        await RollbackDisabledAsync(disabled, steps).ConfigureAwait(false);
                        return Failure(steps, disableResult.Result);
                    }

                    steps.Add(Step(EnforcementStepType.DisableService, serviceName, disableResult.Result));
                    disabled.Add((serviceName, disableResult.Before!));
                }

                foreach (var taskPath in enforcement.CompanionTasks ?? [])
                {
                    var disableResult = DisableCompanionTask(taskPath);
                    steps.Add(Step(EnforcementStepType.DisableScheduledTask, taskPath, disableResult.Result));
                    if (!disableResult.Result.IsSuccess)
                    {
                        RollbackDisabledTasks(disabledTasks, steps);
                        await RollbackDisabledAsync(disabled, steps).ConfigureAwait(false);
                        return Failure(steps, disableResult.Result);
                    }

                    disabledTasks.Add((taskPath, disableResult.WasEnabled));
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation still escapes (26-5 convention), but never with companions
                // left disabled.
                RollbackDisabledTasks(disabledTasks, steps);
                await RollbackDisabledAsync(disabled, steps).ConfigureAwait(false);
                throw;
            }

            // Cleared BEFORE the primary write (epics.md L2234 step order) so the
            // orchestrator can never keep serving stale cached policy. A cleared cache
            // is derived state and is never restored; Windows's refresh task rebuilds
            // it from the policy hive, which on failure is still unchanged (26-8 rule).
            foreach (var cachePath in enforcement.GPCacheEntries ?? [])
            {
                var clear = ClearGPCacheEntry(cachePath);
                steps.Add(Step(EnforcementStepType.ClearGPCache, cachePath, clear));
                if (!clear.IsSuccess)
                {
                    RollbackDisabledTasks(disabledTasks, steps);
                    await RollbackDisabledAsync(disabled, steps).ConfigureAwait(false);
                    return Failure(steps, clear);
                }
            }

            // A throwing delegate must take the same rollback path as a failing one.
            OperationResult<bool> primary;
            try
            {
                primary = await primaryAction(change).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                RollbackDisabledTasks(disabledTasks, steps);
                await RollbackDisabledAsync(disabled, steps).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                primary = OperationResult<bool>.Failure(
                    $"Primary mutation threw: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
            }

            steps.Add(Step(EnforcementStepType.PrimaryMutation, change.SystemLocation, primary));
            if (!primary.IsSuccess)
            {
                RollbackDisabledTasks(disabledTasks, steps);
                await RollbackDisabledAsync(disabled, steps).ConfigureAwait(false);
                return Failure(steps, primary);
            }

            return new EnforcementResult { IsSuccess = true, Steps = steps };
        }
    }

    public async Task<EnforcementResult> RevertAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertPrimary,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(revertPrimary);

        try
        {
            var enforcement = change.Enforcement;
            if (enforcement is null)
                return await RunPrimaryOnlyAsync(change, revertPrimary).ConfigureAwait(false);

            if (Gate(change, enforcement) is { } gateFailure)
                return gateFailure;

            // Reverting a restore-direction change means re-hardening: run the
            // disable-shaped sequence ExecuteAsync uses for configure changes.
            if (enforcement.RestoresCompanions)
                return await RunConfigureShapedAsync(change, enforcement, revertPrimary, cancellationToken).ConfigureAwait(false);

            return await RunRestoreShapedAsync(change, enforcement, revertPrimary).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new EnforcementResult
            {
                IsSuccess = false,
                ErrorMessage = $"Unexpected enforcement revert error for '{change.SettingId}': {ex.Message}",
                ErrorCategory = ErrorCategory.ServiceUnavailable,
            };
        }
    }

    /// <summary>
    /// Restore-shaped sequence: primary first, then GPCache clear, then companion
    /// re-enable. Used by RevertAsync for configure changes and by ExecuteAsync for
    /// changes flagged <see cref="SettingEnforcement.RestoresCompanions"/>.
    /// </summary>
    private async Task<EnforcementResult> RunRestoreShapedAsync(
        ChangeDescriptor change,
        SettingEnforcement enforcement,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> primaryAction)
    {
        var steps = new List<EnforcementStepResult>();

        // Reverse of the configure order: primary first, then companion restore.
        var primary = await primaryAction(change).ConfigureAwait(false);
        steps.Add(Step(EnforcementStepType.PrimaryMutation, change.SystemLocation, primary));
        if (!primary.IsSuccess)
            return Failure(steps, primary);

        // The cache must not keep serving values from the now-reverted policy
        // state; clear it again so Windows rebuilds from the restored hive.
        foreach (var cachePath in enforcement.GPCacheEntries ?? [])
        {
            var clear = ClearGPCacheEntry(cachePath);
            steps.Add(Step(EnforcementStepType.ClearGPCache, cachePath, clear));
            if (!clear.IsSuccess)
                return Failure(steps, clear);
        }

        // Best-effort restore: the true pre-apply start type is unknown here.
        // Manual makes the service startable again without forcing it on; services not
        // currently Disabled are left untouched.
        foreach (var serviceName in enforcement.CompanionServices ?? [])
        {
            var restore = RestoreCompanionToManual(serviceName);
            steps.Add(Step(EnforcementStepType.EnableService, serviceName, restore));
            if (!restore.IsSuccess)
                return Failure(steps, restore);
        }

        // Best-effort re-enable: a still-disabled companion task is turned back on.
        foreach (var taskPath in enforcement.CompanionTasks ?? [])
        {
            var restore = RestoreCompanionTask(taskPath);
            steps.Add(Step(EnforcementStepType.EnableScheduledTask, taskPath, restore));
            if (!restore.IsSuccess)
                return Failure(steps, restore);
        }

        return new EnforcementResult { IsSuccess = true, Steps = steps };
    }

    private EnforcementResult? Gate(ChangeDescriptor change, SettingEnforcement enforcement)
    {
        if (enforcement.OwnerModeRequired)
            return GateFailure(
                $"'{change.DisplayName}' requires the Owner Mode service, which is not yet available.",
                ErrorCategory.OwnerModeRequired);

        // SkuRestriction is deliberately NOT gated: per architecture (SKU detection &
        // gating, FR129) it marks a setting as cosmetic/ineffective on that edition;
        // the UI informs, the user can still apply. Interop layers may still surface
        // ErrorCategory.SkuRestricted for features genuinely absent on an edition.

        // CompanionTasks are executed when a task service is available (Story 3-4);
        // the gate remains only for hosts constructed without one.
        if (enforcement.CompanionTasks is { Count: > 0 } && _scheduledTasks is null)
            return GateFailure(
                $"'{change.DisplayName}' requires scheduled-task enforcement, which is not yet supported.",
                ErrorCategory.ServiceUnavailable);

        if (enforcement.GPCacheEntries is { Count: > 0 })
        {
            if (_registry is null)
                return GateFailure(
                    $"'{change.DisplayName}' requires Group Policy cache synchronization, which is not available in this host.",
                    ErrorCategory.ServiceUnavailable);

            // Safety guard: cache entries are recursively DELETED. A hand-edited user
            // set must never be able to point this at an arbitrary subtree, so every
            // path must be hive-rooted, at least three segments deep, and contain a
            // literal GPCache segment. Reject before any step executes.
            foreach (var entry in enforcement.GPCacheEntries)
            {
                if (!IsSafeGPCachePath(entry))
                    return GateFailure(
                        $"'{change.DisplayName}' has an invalid GPCache entry '{entry}': paths must be hive-rooted, at least three levels deep, and contain a 'GPCache' segment.",
                        ErrorCategory.EnforcementBlocked);
            }
        }

        if (enforcement.AclElevation)
            return GateFailure(
                $"'{change.DisplayName}' requires registry ownership transfer, which is not yet supported.",
                ErrorCategory.ServiceUnavailable);

        return null;
    }

    private async Task<(OperationResult<bool> Result, ServiceStatusInfo? Before)> DisableCompanionAsync(
        string serviceName, CancellationToken cancellationToken)
    {
        var query = _serviceControl.Query(serviceName);
        if (!query.IsSuccess)
            return (OperationResult<bool>.Failure(
                query.ErrorMessage!, query.ErrorCategory!.Value, query.Exception), null);

        var before = query.Value!;

        if (before.State != ServiceState.Stopped)
        {
            var stop = await _serviceControl.StopAsync(serviceName, ServiceStopTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (!stop.IsSuccess)
                return (stop, null);
        }

        var disable = _serviceControl.SetStartType(serviceName, ServiceStartType.Disabled);
        if (!disable.IsSuccess)
        {
            // The stop above already mutated a running service; restore it before failing
            // so the caller never inherits a silently stopped companion.
            if (before.State == ServiceState.Running)
            {
                var restart = await _serviceControl
                    .StartAsync(serviceName, ServiceStopTimeout, CancellationToken.None)
                    .ConfigureAwait(false);
                if (!restart.IsSuccess)
                    return (OperationResult<bool>.Failure(
                        $"{disable.ErrorMessage} Companion service '{serviceName}' was stopped and could not be restarted: {restart.ErrorMessage}",
                        disable.ErrorCategory!.Value, disable.Exception), null);
            }
            return (disable, null);
        }

        return (OperationResult<bool>.Success(true), before);
    }

    private async Task RollbackDisabledAsync(
        List<(string Name, ServiceStatusInfo Before)> disabled, List<EnforcementStepResult> steps)
    {
        for (var i = disabled.Count - 1; i >= 0; i--)
        {
            var (name, before) = disabled[i];
            // Rollback is best-effort and must not throw; cancellation no longer applies.
            var restored = _serviceControl.SetStartType(name, before.StartType);
            if (!restored.IsSuccess)
                Log.Warn("Enforcement rollback: failed to restore start type of {Service}: {Error}", name, restored.ErrorMessage);
            if (before.State == ServiceState.Running)
            {
                var restarted = await _serviceControl.StartAsync(name, ServiceStopTimeout, CancellationToken.None)
                    .ConfigureAwait(false);
                if (!restarted.IsSuccess)
                {
                    Log.Warn("Enforcement rollback: failed to restart {Service}: {Error}", name, restarted.ErrorMessage);
                    restored = restarted;
                }
            }

            // Only report WasRolledBack when the restore actually succeeded.
            if (restored.IsSuccess)
            {
                var stepIndex = steps.FindIndex(s =>
                    s.StepType == EnforcementStepType.DisableService && s.Target == name
                    && s.IsSuccess && !s.WasRolledBack);
                if (stepIndex >= 0)
                    steps[stepIndex] = steps[stepIndex] with { WasRolledBack = true };
            }
        }
    }

    private (OperationResult<bool> Result, bool WasEnabled) DisableCompanionTask(string taskPath)
    {
        // Gate() guarantees _scheduledTasks is non-null whenever CompanionTasks execute.
        var query = _scheduledTasks!.Query(taskPath);
        if (!query.IsSuccess)
            return (OperationResult<bool>.Failure(
                query.ErrorMessage!, query.ErrorCategory!.Value, query.Exception), false);

        var wasEnabled = query.Value!.IsEnabled;
        if (!wasEnabled)
            return (OperationResult<bool>.Success(true), false);

        return (_scheduledTasks.SetEnabled(taskPath, false), true);
    }

    private void RollbackDisabledTasks(
        List<(string Path, bool WasEnabled)> disabledTasks, List<EnforcementStepResult> steps)
    {
        for (var i = disabledTasks.Count - 1; i >= 0; i--)
        {
            var (path, wasEnabled) = disabledTasks[i];
            if (!wasEnabled)
                continue; // was already disabled before we touched it

            var restored = _scheduledTasks!.SetEnabled(path, true);
            if (!restored.IsSuccess)
            {
                Log.Warn("Enforcement rollback: failed to re-enable task {Task}: {Error}", path, restored.ErrorMessage);
                continue;
            }

            var stepIndex = steps.FindIndex(s =>
                s.StepType == EnforcementStepType.DisableScheduledTask && s.Target == path
                && s.IsSuccess && !s.WasRolledBack);
            if (stepIndex >= 0)
                steps[stepIndex] = steps[stepIndex] with { WasRolledBack = true };
        }
    }

    private OperationResult<bool> RestoreCompanionTask(string taskPath)
    {
        var query = _scheduledTasks!.Query(taskPath);
        if (!query.IsSuccess)
            return OperationResult<bool>.Failure(
                query.ErrorMessage!, query.ErrorCategory!.Value, query.Exception);

        if (query.Value!.IsEnabled)
            return OperationResult<bool>.Success(true);

        return _scheduledTasks.SetEnabled(taskPath, true);
    }

    private OperationResult<bool> ClearGPCacheEntry(string cachePath)
    {
        // Gate() guarantees _registry is non-null whenever GPCacheEntries execute.
        var exists = _registry!.KeyExists(cachePath);
        if (!exists.IsSuccess)
            return OperationResult<bool>.Failure(
                exists.ErrorMessage!, exists.ErrorCategory!.Value, exists.Exception);

        if (!exists.Value)
            return OperationResult<bool>.Success(true); // nothing cached; nothing to clear

        return _registry.DeleteKey(cachePath, recursive: true);
    }

    // Mirrors RegistryService.ParseKeyPath's accepted roots; the guard must reject
    // up front anything the registry layer would reject (or misroute) at run time.
    private static readonly string[] KnownHives =
    [
        "HKCU", "HKEY_CURRENT_USER",
        "HKLM", "HKEY_LOCAL_MACHINE",
        "HKCR", "HKEY_CLASSES_ROOT",
        "HKU", "HKEY_USERS",
        "HKCC", "HKEY_CURRENT_CONFIG",
    ];

    private static bool IsSafeGPCachePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        // Hive + at least three levels, e.g. HKLM\SOFTWARE\Microsoft\...\GPCache.
        if (segments.Length < 4)
            return false;

        if (Array.FindIndex(KnownHives, h => string.Equals(h, segments[0], StringComparison.OrdinalIgnoreCase)) < 0)
            return false;

        for (var i = 1; i < segments.Length; i++)
        {
            if (string.Equals(segments[i], "GPCache", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private OperationResult<bool> RestoreCompanionToManual(string serviceName)
    {
        var query = _serviceControl.Query(serviceName);
        if (!query.IsSuccess)
            return OperationResult<bool>.Failure(
                query.ErrorMessage!, query.ErrorCategory!.Value, query.Exception);

        if (query.Value!.StartType != ServiceStartType.Disabled)
            return OperationResult<bool>.Success(true);

        return _serviceControl.SetStartType(serviceName, ServiceStartType.Manual);
    }

    private static async Task<EnforcementResult> RunPrimaryOnlyAsync(
        ChangeDescriptor change, Func<ChangeDescriptor, Task<OperationResult<bool>>> primary)
    {
        var result = await primary(change).ConfigureAwait(false);
        var step = Step(EnforcementStepType.PrimaryMutation, change.SystemLocation, result);
        return result.IsSuccess
            ? new EnforcementResult { IsSuccess = true, Steps = [step] }
            : Failure([step], result);
    }

    private static EnforcementStepResult Step(
        EnforcementStepType type, string target, OperationResult<bool> result) => new()
    {
        StepType = type,
        Target = target,
        IsSuccess = result.IsSuccess,
        ErrorMessage = result.ErrorMessage,
    };

    private static EnforcementResult Failure(List<EnforcementStepResult> steps, OperationResult<bool> failed) => new()
    {
        IsSuccess = false,
        Steps = steps,
        ErrorMessage = failed.ErrorMessage,
        ErrorCategory = failed.ErrorCategory ?? ErrorCategory.ServiceUnavailable,
    };

    private static EnforcementResult GateFailure(string message, ErrorCategory category) => new()
    {
        IsSuccess = false,
        ErrorMessage = message,
        ErrorCategory = category,
    };
}
