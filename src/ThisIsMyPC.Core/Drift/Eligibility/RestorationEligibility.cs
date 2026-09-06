namespace ThisIsMyPC.Core.Drift.Eligibility;

/// <summary>Exact catalog location and user that own management and retry evidence.</summary>
public sealed record RestorationIdentity(string ModuleId, string SettingId, string KeyPath, string ValueName, string UserSid)
{
    /// <summary>Uses canonical identity from a catalog-validated candidate.</summary>
    public static RestorationIdentity FromCandidate(RestorationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new(candidate.Target.ModuleId, candidate.Target.SettingId, candidate.Target.KeyPath,
            candidate.Target.ValueName, candidate.UserSid ?? string.Empty);
    }
}

/// <summary>Evidence from a profile inventory, not a conclusion derived from SID syntax.</summary>
public enum RestorationProfileState { Unknown, Unsupported, Unloaded, SupportedAndLoaded }

/// <summary>State of one identified user profile. Unknown evidence never authorizes restoration.</summary>
public sealed record RestorationProfileEvidence(string UserSid, RestorationProfileState State);

/// <summary>Management evidence must establish that this exact target is unmanaged.</summary>
public enum RestorationManagementState { Unknown, Managed, Unmanaged }

/// <summary>Management assessment for one exact target and user, including preference targets.</summary>
public sealed record RestorationManagementEvidence(RestorationIdentity Identity, RestorationManagementState State);

/// <summary>
/// Projection of one failed or repeatedly reverted attempt from trusted history.
/// An attempt appears once even when its failure and later reversion both qualify.
/// This is a policy input, not a journal record or a persistence format.
/// </summary>
public sealed record RestorationRetryObservation(RestorationIdentity Identity, Guid AttemptId, DateTimeOffset OccurredAt);

/// <summary>An explicit retry reset for one target and user. It does not grant machine consent.</summary>
public sealed record RestorationRetryReset(RestorationIdentity Identity, DateTimeOffset ResetAt);

/// <summary>Evidence for one decision. Consent defaults off; missing evidence fails closed.</summary>
public sealed record RestorationEligibilityRequest
{
    public required DriftBaselineEntry Entry { get; init; }
    public string? UserSid { get; init; }
    public bool MachineConsentGranted { get; init; }
    public RestorationProfileEvidence? Profile { get; init; }
    public RestorationManagementEvidence? Management { get; init; }
    public bool RetryHistoryComplete { get; init; }
    public IReadOnlyList<RestorationRetryObservation> RetryHistory { get; init; } = [];
    public RestorationRetryReset? RetryReset { get; init; }
}

/// <summary>The first gate that prevented restoration, or Eligible when every gate passed.</summary>
public enum RestorationEligibilityOutcome
{
    Eligible, ConsentMissing, InvalidTarget, ProfileUnknown, ProfileUnsupported, ProfileUnloaded,
    ManagementUnknown, Managed, RetryHistoryUnknown, RetryEvidenceInvalid, RetryLimitReached,
}

/// <summary>Immutable decision evidence. Eligibility is not permission to skip the mutation lock or a fresh consent check.</summary>
public sealed record RestorationEligibilityDecision
{
    public required RestorationEligibilityOutcome Outcome { get; init; }
    public required string Detail { get; init; }
    public required DateTimeOffset EvaluatedAt { get; init; }
    public RestorationCandidate? Candidate { get; init; }
    public RestorationValidationOutcome? ValidationOutcome { get; init; }
    public int CountedAttempts { get; init; }
    public bool IsEligible => Outcome == RestorationEligibilityOutcome.Eligible;
}