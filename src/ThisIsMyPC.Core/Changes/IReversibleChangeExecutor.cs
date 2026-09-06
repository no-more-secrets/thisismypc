using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Changes;

/// <summary>
/// Runs one reversible <see cref="ChangeDescriptor"/> in the apply or revert
/// direction with the routing rule every writer in the product shares:
/// <c>Enforcement != null</c> goes through <see cref="Enforcement.IEnforcementExecutor"/>,
/// <c>null</c> goes straight to the supplied module delegate. Nothing else is
/// inferred from the descriptor; the executor never picks a module or a
/// registry writer on its own. The pending-changes queue, history undo and
/// redo, and Owner Mode restoration all execute through this contract so a
/// value written by the service is written by the same routing the app uses.
/// There is no cancellation token on purpose: the batch checks its token between
/// changes and never hands it down, so a change in flight always completes and a
/// token here would only reach enforced changes, never a bare delegate.
/// </summary>
public interface IReversibleChangeExecutor
{
    /// <summary>
    /// False only when <paramref name="change"/> carries enforcement and no
    /// enforcement executor is configured. Callers that must fail before any
    /// write (a batch) check this up front; single-descriptor callers may skip
    /// it and receive a failed result instead.
    /// </summary>
    bool CanExecute(ChangeDescriptor change);

    /// <summary>Applies the descriptor (writes <see cref="ChangeDescriptor.AfterValue"/>).</summary>
    Task<OperationResult<bool>> ApplyAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc);

    /// <summary>
    /// Reverts the descriptor. The delegate contract is "apply the descriptor's
    /// AfterValue", so callers pass a Before/After-swapped descriptor, as
    /// history undo and mid-group rollback already do.
    /// </summary>
    Task<OperationResult<bool>> RevertAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc);
}
