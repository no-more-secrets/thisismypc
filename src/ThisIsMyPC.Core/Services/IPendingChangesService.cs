using System.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

public interface IPendingChangesService : INotifyPropertyChanged
{
    int PendingCount { get; }
    IReadOnlyList<ChangeGroup> PendingGroups { get; }
    bool IsApplying { get; }

    /// <summary>
    /// Staged groups whose live state is unknown after a batch stopped inside
    /// them. They stay in <see cref="PendingGroups"/> so the person can see and
    /// discard them, but no batch applies while any is staged. Cleared only by
    /// <see cref="DiscardAll"/>. Unstage retains marked groups. The default is empty
    /// for implementations that never mark a group.
    /// </summary>
    IReadOnlyList<GroupReconciliation> ReconciliationRequired => [];

    void Stage(ChangeDescriptor change);
    void Stage(ChangeGroup group);
    /// <summary>Removes a clean group. A group requiring reconciliation remains staged until DiscardAll.</summary>
    void Unstage(string groupId);
    void DiscardAll();

    /// <summary>
    /// Applies every staged group in order without cancellation. Equivalent to the
    /// token overload with <see cref="CancellationToken.None"/>.
    /// </summary>
    Task<MutationResult> ApplyAllAsync(
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc);

    /// <summary>
    /// Applies every staged group in order, stopping cooperatively when
    /// <paramref name="cancellationToken"/> is signalled. The token is checked before
    /// each change starts; it is never handed to a delegate, so a change in flight
    /// always runs to completion, and rollback never observes it. On cancellation the
    /// current group's applied changes are reverted and the group stays staged;
    /// groups that completed earlier stay applied, leave the queue, and are returned
    /// in <see cref="MutationResult.Applied"/>. While any group is in
    /// <see cref="ReconciliationRequired"/> the batch returns
    /// <see cref="MutationFailureKind.ReconciliationRequired"/> without invoking a writer.
    /// </summary>
    /// <remarks>
    /// The default implementation forwards to the two-argument overload only for a
    /// token that cannot be cancelled. A cancellable token against an implementation
    /// that does not override this member throws <see cref="NotSupportedException"/>,
    /// so a caller never believes a batch is cancellable when it is not.
    /// </remarks>
    Task<MutationResult> ApplyAllAsync(
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            throw new NotSupportedException(
                $"{GetType().Name} does not support batch cancellation; override the token overload of ApplyAllAsync.");
        }

        return ApplyAllAsync(applyFunc, revertFunc);
    }
}
