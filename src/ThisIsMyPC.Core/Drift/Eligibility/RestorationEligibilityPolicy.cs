namespace ThisIsMyPC.Core.Drift.Eligibility;

/// <summary>Pure eligibility and retry policy. The caller supplies trusted, current evidence and owns all I/O.</summary>
public sealed class RestorationEligibilityPolicy(TimeProvider timeProvider)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    public const int AttemptLimit = 3;
    public static readonly TimeSpan RetryWindow = TimeSpan.FromDays(7);

    /// <summary>Checks consent, the shipped catalog, profile support, target management, and retry history in that order.</summary>
    public RestorationEligibilityDecision Evaluate(RestorationEligibilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Entry);
        var now = _timeProvider.GetUtcNow();
        if (!request.MachineConsentGranted)
            return Decide(RestorationEligibilityOutcome.ConsentMissing, "Machine restoration consent is off or missing.");

        var validation = RestorationCatalog.Default.Validate(request.Entry, request.UserSid);
        if (!validation.IsAccepted)
        {
            return Decide(RestorationEligibilityOutcome.InvalidTarget, validation.Detail) with
            {
                ValidationOutcome = validation.Outcome,
            };
        }

        var candidate = validation.Candidate!;
        var identity = RestorationIdentity.FromCandidate(candidate);
        if (request.Profile is not { } profile || profile.UserSid != identity.UserSid)
            return Decide(RestorationEligibilityOutcome.ProfileUnknown, "No profile evidence matches the target user.");
        switch (profile.State)
        {
            case RestorationProfileState.Unsupported:
                return Decide(RestorationEligibilityOutcome.ProfileUnsupported, "The target profile is unsupported.");
            case RestorationProfileState.Unloaded:
                return Decide(RestorationEligibilityOutcome.ProfileUnloaded, "The target profile hive is not loaded.");
            case RestorationProfileState.SupportedAndLoaded:
                break;
            default:
                return Decide(RestorationEligibilityOutcome.ProfileUnknown, "Profile support and loaded state are unknown.");
        }

        if (request.Management is not { } management || management.Identity != identity)
            return Decide(RestorationEligibilityOutcome.ManagementUnknown, "No management evidence matches this target and user.");
        if (management.State == RestorationManagementState.Managed)
            return Decide(RestorationEligibilityOutcome.Managed, "This target is managed and must not be restored.");
        if (management.State != RestorationManagementState.Unmanaged)
            return Decide(RestorationEligibilityOutcome.ManagementUnknown, "Management is unknown, including for preference targets.");

        if (!request.RetryHistoryComplete || request.RetryHistory is null)
            return Decide(RestorationEligibilityOutcome.RetryHistoryUnknown, "Complete retry history is unavailable.");

        var resetAt = request.RetryReset?.Identity == identity ? request.RetryReset.ResetAt : (DateTimeOffset?)null;
        if (resetAt > now)
            return Decide(RestorationEligibilityOutcome.RetryEvidenceInvalid, "The target retry reset is dated in the future.");

        // An ID identifies one attempt. Conflicting timestamps cannot safely establish the retry window.
        var attempts = new Dictionary<Guid, DateTimeOffset>();
        foreach (var observation in request.RetryHistory)
        {
            if (observation is null)
                return Decide(RestorationEligibilityOutcome.RetryEvidenceInvalid, "Retry history contains a missing observation.");
            if (observation.Identity != identity)
                continue;
            if (observation.AttemptId == Guid.Empty || observation.OccurredAt > now)
                return Decide(RestorationEligibilityOutcome.RetryEvidenceInvalid, "A target retry observation has an invalid ID or future timestamp.");
            if (attempts.TryGetValue(observation.AttemptId, out var prior) && prior != observation.OccurredAt)
                return Decide(RestorationEligibilityOutcome.RetryEvidenceInvalid, "One target attempt has conflicting timestamps.");
            attempts[observation.AttemptId] = observation.OccurredAt;
        }

        var count = attempts.Values.Count(at => now - at <= RetryWindow && (resetAt is null || at > resetAt));
        return Decide(count >= AttemptLimit ? RestorationEligibilityOutcome.RetryLimitReached : RestorationEligibilityOutcome.Eligible,
            count >= AttemptLimit
                ? "Three failed or repeatedly reverted attempts occurred within seven days after the last reset."
                : "Consent, target, profile, management, and retry evidence permit restoration.") with
        {
            Candidate = candidate,
            CountedAttempts = count,
        };

        RestorationEligibilityDecision Decide(RestorationEligibilityOutcome outcome, string detail) => new()
        {
            Outcome = outcome,
            Detail = detail,
            EvaluatedAt = now,
        };
    }
}