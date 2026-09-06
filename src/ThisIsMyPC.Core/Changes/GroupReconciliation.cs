namespace ThisIsMyPC.Core.Changes;

/// <summary>
/// A staged group whose live state is unknown after a batch stopped inside it.
/// The queue keeps the group visible, keeps this record beside it, and refuses
/// to apply anything while it is staged. Only <c>Unstage</c> or <c>DiscardAll</c>
/// clears it; the caller then reads the live values and stages a fresh group.
/// </summary>
public sealed record GroupReconciliation
{
    public required ChangeGroup Group { get; init; }

    /// <summary>ChangeFailed, ChangeThrew, or Cancelled: how the batch stopped.</summary>
    public required MutationFailureKind Kind { get; init; }

    /// <summary>The change the batch stopped at; null when it stopped on cancellation.</summary>
    public ChangeDescriptor? Failed { get; init; }

    /// <summary>Changes whose live value is unknown; never empty.</summary>
    public required IReadOnlyList<ChangeDescriptor> Uncertain { get; init; }

    /// <summary>Changes whose revert returned success.</summary>
    public required IReadOnlyList<ChangeDescriptor> RolledBack { get; init; }

    public required IReadOnlyList<RollbackFailure> RollbackFailures { get; init; }

    public string? ErrorMessage { get; init; }

    public Exception? Exception { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }
}
