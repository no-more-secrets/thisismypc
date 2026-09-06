using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Core.Drift;

public enum RestorationPreparationOutcome
{
    /// <summary>A descriptor was built; run it through the shared executor.</summary>
    Ready,
    /// <summary>
    /// The candidate does not match the shipped catalog: its target is not the
    /// catalog's own instance, its desired value is not one of that target's
    /// allowed canonical values, its SID is not an account SID, or its resolved
    /// path is not the one derived from the target and SID. A candidate is a
    /// record and can be rebuilt with <c>with</c>, so preparation rechecks it.
    /// </summary>
    UntrustedCandidate,
    /// <summary>The observed value already equals the desired value; nothing to write.</summary>
    AlreadyMatches,
    /// <summary>
    /// The value is absent. <see cref="ChangeDescriptor.BeforeValue"/> has no
    /// absent state that is not a sentinel string, so an absent before-state is
    /// refused until the descriptor contract carries one explicitly.
    /// </summary>
    ObservedAbsent,
    /// <summary>The value is present but not the target's kind, or its data does not canonicalize.</summary>
    ObservedKindMismatch,
}

/// <summary>
/// The result of <see cref="RestorationBatchFactory.Prepare"/>. Issued only by
/// that factory: this is a sealed class, not a record, with a private
/// constructor, get-only properties, and internal creation methods, so no code
/// outside the Core assembly can build a Ready preparation around a descriptor
/// the factory did not check. Core has no InternalsVisibleTo; the assembly is
/// the trust boundary. <see cref="RestorationBatchFactory.CreateGroup"/> relies
/// on this and does not revalidate descriptors.
/// </summary>
public sealed class RestorationPreparation
{
    private RestorationPreparation(RestorationPreparationOutcome outcome, string detail, ChangeDescriptor? change)
    {
        Outcome = outcome;
        Detail = detail;
        Change = change;
    }

    public RestorationPreparationOutcome Outcome { get; }
    public string Detail { get; }

    /// <summary>The descriptor to run; non-null exactly when <see cref="IsReady"/>.</summary>
    public ChangeDescriptor? Change { get; }

    public bool IsReady => Outcome == RestorationPreparationOutcome.Ready && Change is not null;

    internal static RestorationPreparation Ready(ChangeDescriptor change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return new RestorationPreparation(RestorationPreparationOutcome.Ready, string.Empty, change);
    }

    internal static RestorationPreparation Reject(RestorationPreparationOutcome outcome, string detail)
    {
        if (outcome == RestorationPreparationOutcome.Ready)
            throw new ArgumentException("A rejection cannot carry the Ready outcome.", nameof(outcome));
        ArgumentNullException.ThrowIfNull(detail);
        return new RestorationPreparation(outcome, detail, null);
    }
}
