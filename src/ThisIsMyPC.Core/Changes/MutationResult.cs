using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Changes;

/// <summary>
/// Outcome of one reversible batch. <see cref="Applied"/> holds every change from
/// a group that completed; those groups left the queue, are trusted on the
/// strength of their success results, and must be recorded, never replayed.
/// <see cref="Failed"/> is the change the batch stopped at. <see cref="RolledBack"/>
/// lists the current group's changes whose revert returned success;
/// <see cref="RollbackFailures"/> the ones that did not. <see cref="Uncertain"/>
/// lists every change whose live value is unknown: the failed or thrown change
/// (a failed result is not proof of no write) and every rollback failure. A
/// non-empty <see cref="Uncertain"/> list means the group was left in the queue
/// marked as needing reconciliation and will not apply again until discarded.
/// </summary>
public record MutationResult
{
    public bool IsSuccess { get; init; }
    public required IReadOnlyList<ChangeDescriptor> Applied { get; init; }
    public ChangeDescriptor? Failed { get; init; }
    public required IReadOnlyList<ChangeDescriptor> RolledBack { get; init; }
    public string? ErrorMessage { get; init; }
    public ErrorCategory? ErrorCategory { get; init; }
    public IReadOnlyList<RestartRequirement> RequiredRestarts { get; init; } = [];

    /// <summary>Why the batch stopped; <see cref="MutationFailureKind.None"/> on success.</summary>
    public MutationFailureKind FailureKind { get; init; } = MutationFailureKind.None;

    /// <summary>The exception a change's apply threw, when <see cref="FailureKind"/> is ChangeThrew.</summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Changes from the current group that applied and were not reverted. Each
    /// carries the revert's message and, when it threw, the exception. The live
    /// value of every entry is uncertain.
    /// </summary>
    public IReadOnlyList<RollbackFailure> RollbackFailures { get; init; } = [];

    /// <summary>
    /// Every change whose live value is unknown after this batch: the change that
    /// failed or threw, plus each rollback failure's change, in group order. Their
    /// descriptors' before values may be stale. A caller must not record, undo,
    /// or retry them as if their state were known; discard the group and stage
    /// it again from a fresh read.
    /// </summary>
    public IReadOnlyList<ChangeDescriptor> Uncertain { get; init; } = [];

    public bool WasCancelled => FailureKind == MutationFailureKind.Cancelled;

    /// <summary>True when <see cref="Uncertain"/> is non-empty.</summary>
    public bool HasUncertainState => Uncertain.Count > 0;
}
