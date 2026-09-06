namespace ThisIsMyPC.Core.Changes;

/// <summary>
/// Why a reversible batch stopped. <see cref="MutationResult.IsSuccess"/> is
/// false for every value except <see cref="None"/>.
/// </summary>
public enum MutationFailureKind
{
    /// <summary>Every staged group applied.</summary>
    None,

    /// <summary>
    /// A change returned a failed result. A failed result says nothing about the
    /// live value (a module catches its own exceptions, and enforcement can fail
    /// after its primary write succeeded), so the change is uncertain. The group's
    /// earlier changes were rolled back and the group needs reconciliation.
    /// </summary>
    ChangeFailed,

    /// <summary>
    /// A change's delegate or enforcement executor threw. The change may or may not
    /// have written; it is reported in <see cref="MutationResult.Failed"/>, is never
    /// reverted and never counted as rolled back, and its group needs reconciliation.
    /// </summary>
    ChangeThrew,

    /// <summary>
    /// The batch's cancellation token was signalled. No change was started after the
    /// signal; the change in flight was awaited to completion, and the current
    /// group's applied changes were rolled back. Groups that had already completed
    /// stay applied and are reported in <see cref="MutationResult.Applied"/>. The
    /// group needs reconciliation only when a rollback failed.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The batch refused to start because a staged group still needs reconciliation
    /// from an earlier stop. No writer was invoked. Discard that group and stage it
    /// again from a fresh read before applying.
    /// </summary>
    ReconciliationRequired,
}
