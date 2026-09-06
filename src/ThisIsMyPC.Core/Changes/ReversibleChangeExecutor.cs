using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Changes;

/// <summary>
/// The one implementation of <see cref="IReversibleChangeExecutor"/>. Holds an
/// optional <see cref="IEnforcementExecutor"/>; an enforced descriptor with no
/// executor configured is refused with a failed result and never degrades to a
/// bare delegate call (companion services, tasks, and GPCache entries would stay
/// mutated). Exceptions from the delegate or the enforcement executor propagate
/// unchanged.
/// </summary>
public sealed class ReversibleChangeExecutor : IReversibleChangeExecutor
{
    private readonly IEnforcementExecutor? _enforcementExecutor;

    public ReversibleChangeExecutor(IEnforcementExecutor? enforcementExecutor = null)
    {
        _enforcementExecutor = enforcementExecutor;
    }

    public bool CanExecute(ChangeDescriptor change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return change.Enforcement is null || _enforcementExecutor is not null;
    }

    public Task<OperationResult<bool>> ApplyAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc)
        => RouteAsync(change, applyFunc, revert: false);

    public Task<OperationResult<bool>> RevertAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc)
        => RouteAsync(change, revertFunc, revert: true);

    private async Task<OperationResult<bool>> RouteAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> primaryFunc,
        bool revert)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(primaryFunc);

        // Enforcement != null routes through the executor; null goes directly to the
        // module delegate. No other heuristics (architecture.md L913/L973).
        if (change.Enforcement is null)
            return await primaryFunc(change).ConfigureAwait(false);

        if (_enforcementExecutor is null)
        {
            return OperationResult<bool>.Failure(
                $"'{change.DisplayName}' requires enforcement but no IEnforcementExecutor is configured.",
                ErrorCategory.ServiceUnavailable);
        }

        // No token, exactly as the pending queue and history called the enforcement
        // executor before this extraction.
        var enforcement = revert
            ? await _enforcementExecutor.RevertAsync(change, primaryFunc).ConfigureAwait(false)
            : await _enforcementExecutor.ExecuteAsync(change, primaryFunc).ConfigureAwait(false);

        return enforcement.IsSuccess
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure(
                enforcement.ErrorMessage ?? (revert ? "Enforcement revert failed" : "Enforcement execution failed"),
                enforcement.ErrorCategory ?? ErrorCategory.ServiceUnavailable);
    }
}
