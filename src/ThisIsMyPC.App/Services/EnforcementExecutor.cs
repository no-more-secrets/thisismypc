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
/// primary mutation (supplied as a delegate — the executor never calls modules directly)
/// and restored on failure. Companion scheduled tasks are disabled/re-enabled the same
/// way when a task service is supplied. GPCache and ACL enforcement dimensions are
/// not yet supported and fail up front rather than silently partially enforcing.
/// No exceptions escape except OperationCanceledException on caller cancellation.
/// </summary>
public sealed class EnforcementExecutor : IEnforcementExecutor
{
    private static readonly TimeSpan ServiceStopTimeout = TimeSpan.FromSeconds(30);

    private readonly IServiceControlService _serviceControl;
    private readonly IScheduledTaskService? _scheduledTasks;

    public EnforcementExecutor(IServiceControlService serviceControl, IScheduledTaskService? scheduledTasks = null)
    {
        ArgumentNullException.ThrowIfNull(serviceControl);
        _serviceControl = serviceControl;
        _scheduledTasks = scheduledTasks;
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

            // A throwing delegate must take the same rollback path as a failing one.
            OperationResult<bool> primary;
            try
            {
                primary = await applyPrimary(change).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
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

            var steps = new List<EnforcementStepResult>();

            // Reverse of apply order: primary first, then companion restore.
            var primary = await revertPrimary(change).ConfigureAwait(false);
            steps.Add(Step(EnforcementStepType.PrimaryMutation, change.SystemLocation, primary));
            if (!primary.IsSuccess)
                return Failure(steps, primary);

            // Best-effort restore: the true pre-apply start type is unknown at revert time.
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

    private EnforcementResult? Gate(ChangeDescriptor change, SettingEnforcement enforcement)
    {
        if (enforcement.OwnerModeRequired)
            return GateFailure(
                $"'{change.DisplayName}' requires the Owner Mode service, which is not yet available.",
                ErrorCategory.OwnerModeRequired);

        // SkuRestriction is deliberately NOT gated: per architecture (SKU detection &
        // gating, FR129) it marks a setting as cosmetic/ineffective on that edition —
        // the UI informs, the user can still apply. Interop layers may still surface
        // ErrorCategory.SkuRestricted for features genuinely absent on an edition.

        // CompanionTasks are executed when a task service is available (Story 3-4);
        // the gate remains only for hosts constructed without one.
        if (enforcement.CompanionTasks is { Count: > 0 } && _scheduledTasks is null)
            return GateFailure(
                $"'{change.DisplayName}' requires scheduled-task enforcement, which is not yet supported.",
                ErrorCategory.ServiceUnavailable);

        if (enforcement.GPCacheEntries is { Count: > 0 })
            return GateFailure(
                $"'{change.DisplayName}' requires Group Policy cache synchronization, which is not yet supported.",
                ErrorCategory.ServiceUnavailable);

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
            // The stop above already mutated a running service — restore it before failing
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
                System.Diagnostics.Debug.WriteLine(
                    $"Enforcement rollback: failed to restore start type of '{name}': {restored.ErrorMessage}");
            if (before.State == ServiceState.Running)
            {
                var restarted = await _serviceControl.StartAsync(name, ServiceStopTimeout, CancellationToken.None)
                    .ConfigureAwait(false);
                if (!restarted.IsSuccess)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Enforcement rollback: failed to restart '{name}': {restarted.ErrorMessage}");
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
                System.Diagnostics.Debug.WriteLine(
                    $"Enforcement rollback: failed to re-enable task '{path}': {restored.ErrorMessage}");
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
