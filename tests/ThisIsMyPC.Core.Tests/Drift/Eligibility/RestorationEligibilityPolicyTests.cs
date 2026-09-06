using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Eligibility;

namespace ThisIsMyPC.Core.Tests.Drift.Eligibility;

public sealed class RestorationEligibilityPolicyTests
{
    private const string Sid = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    private const string OtherSid = "S-1-5-21-1111111111-2222222222-3333333333-1002";
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => Current;
    }

    private static RestorationEligibilityRequest Request(int targetIndex = 0, string sid = Sid)
    {
        var target = RestorationCatalog.Default.Targets[targetIndex];
        var entry = new DriftBaselineEntry
        {
            ModuleId = target.ModuleId, SettingId = target.SettingId, DisplayName = target.DisplayName,
            SystemLocation = target.KeyPath + "\\" + target.ValueName, ValueType = target.ValueType,
            ExpectedValue = target.SuppressedValue.Data, UpdatedAtUtc = Now,
        };
        var candidate = RestorationCatalog.Default.Validate(entry, sid).Candidate!;
        return new()
        {
            Entry = entry, UserSid = sid, MachineConsentGranted = true,
            Profile = new(sid, RestorationProfileState.SupportedAndLoaded),
            Management = new(RestorationIdentity.FromCandidate(candidate), RestorationManagementState.Unmanaged),
            RetryHistoryComplete = true,
        };
    }

    private static RestorationRetryObservation Attempt(RestorationEligibilityRequest request, DateTimeOffset at)
        => new(request.Management!.Identity, Guid.NewGuid(), at);
    private static RestorationEligibilityDecision Decide(RestorationEligibilityRequest request)
        => new RestorationEligibilityPolicy(new Clock()).Evaluate(request);

    [Fact]
    public void Consent_defaults_off_and_reset_does_not_enable_it()
    {
        var ready = Request();
        var request = new RestorationEligibilityRequest
        {
            Entry = ready.Entry, UserSid = Sid, Profile = ready.Profile, Management = ready.Management,
            RetryHistoryComplete = true, RetryReset = new(ready.Management!.Identity, Now),
        };
        Assert.Equal(RestorationEligibilityOutcome.ConsentMissing, Decide(request).Outcome);
    }

    [Fact]
    public void Every_shipped_target_requires_explicit_unmanaged_evidence_including_preferences()
    {
        for (var index = 0; index < RestorationCatalog.Default.Targets.Length; index++)
        {
            var request = Request(index);
            Assert.True(Decide(request).IsEligible);
            foreach (var state in new[] { RestorationManagementState.Unknown, (RestorationManagementState)99 })
                Assert.Equal(RestorationEligibilityOutcome.ManagementUnknown,
                    Decide(request with { Management = request.Management! with { State = state } }).Outcome);
            Assert.Equal(RestorationEligibilityOutcome.Managed,
                Decide(request with { Management = request.Management! with { State = RestorationManagementState.Managed } }).Outcome);
            Assert.Equal(RestorationEligibilityOutcome.ManagementUnknown, Decide(request with { Management = null }).Outcome);
        }
    }

    [Theory]
    [InlineData(RestorationProfileState.Unknown, RestorationEligibilityOutcome.ProfileUnknown)]
    [InlineData(RestorationProfileState.Unsupported, RestorationEligibilityOutcome.ProfileUnsupported)]
    [InlineData(RestorationProfileState.Unloaded, RestorationEligibilityOutcome.ProfileUnloaded)]
    [InlineData((RestorationProfileState)99, RestorationEligibilityOutcome.ProfileUnknown)]
    public void Sid_syntax_does_not_prove_supported_loaded_profile(RestorationProfileState state, RestorationEligibilityOutcome outcome)
    {
        Assert.Equal(outcome, Decide(Request() with { Profile = new(Sid, state) }).Outcome);
    }

    [Fact]
    public void Evidence_for_another_user_or_target_cannot_authorize_this_one()
    {
        var request = Request();
        Assert.Equal(RestorationEligibilityOutcome.ProfileUnknown, Decide(request with { Profile = Request(sid: OtherSid).Profile }).Outcome);
        Assert.Equal(RestorationEligibilityOutcome.ProfileUnknown, Decide(request with { Profile = null }).Outcome);
        Assert.Equal(RestorationEligibilityOutcome.ManagementUnknown, Decide(request with { Management = Request(1).Management }).Outcome);
        Assert.Equal(RestorationEligibilityOutcome.ManagementUnknown, Decide(request with { Management = Request(sid: OtherSid).Management }).Outcome);
    }

    [Fact]
    public void Catalog_validation_precedes_evidence_and_returns_its_reason()
    {
        var request = Request();
        var decision = Decide(request with { Entry = request.Entry with { SettingId = "invented" } });
        Assert.Equal(RestorationEligibilityOutcome.InvalidTarget, decision.Outcome);
        Assert.Equal(RestorationValidationOutcome.IdentityMismatch, decision.ValidationOutcome);
        Assert.Null(decision.Candidate);
        Assert.Equal(RestorationEligibilityOutcome.InvalidTarget, Decide(request with { UserSid = "S-1-5-18" }).Outcome);
        Assert.Equal(RestorationEligibilityOutcome.InvalidTarget, Decide(request with { Entry = request.Entry with { ExpectedValue = "77" } }).Outcome);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void Third_adverse_attempt_stops_further_attempts(int count, bool eligible)
    {
        var request = Request();
        request = request with { RetryHistory = Enumerable.Range(0, count).Select(i => Attempt(request, Now.AddHours(-i))).ToArray() };
        var result = Decide(request);
        Assert.Equal(eligible, result.IsEligible);
        Assert.Equal(count, result.CountedAttempts);
        Assert.NotEmpty(result.Detail);
        Assert.Equal(Now, result.EvaluatedAt);
    }

    [Fact]
    public void Seven_day_boundary_is_inclusive_and_injected_time_ages_it_out()
    {
        var request = Request();
        request = request with { RetryHistory = Enumerable.Range(0, 3).Select(_ => Attempt(request, Now.AddDays(-7))).ToArray() };
        var clock = new Clock();
        var policy = new RestorationEligibilityPolicy(clock);
        Assert.Equal(RestorationEligibilityOutcome.RetryLimitReached, policy.Evaluate(request).Outcome);
        clock.Current = Now.AddTicks(1);
        Assert.True(policy.Evaluate(request).IsEligible);
        Assert.Equal(0, policy.Evaluate(request).CountedAttempts);
    }

    [Fact]
    public void Duplicated_attempt_projection_counts_once()
    {
        var request = Request();
        var one = Attempt(request, Now);
        var result = Decide(request with { RetryHistory = [one, one, one] });
        Assert.True(result.IsEligible);
        Assert.Equal(1, result.CountedAttempts);
    }

    [Fact]
    public void Other_targets_and_users_do_not_consume_or_reset_this_budget()
    {
        var request = Request();
        var other = Request(sid: OtherSid);
        var history = new[] { Attempt(request, Now), Attempt(Request(1), Now), Attempt(other, Now.AddDays(1)) };
        var result = Decide(request with { RetryHistory = history, RetryReset = new(other.Management!.Identity, Now.AddDays(1)) });
        Assert.True(result.IsEligible);
        Assert.Equal(1, result.CountedAttempts);
    }

    [Fact]
    public void Explicit_reset_excludes_attempts_at_or_before_it_but_preserves_later_attempts()
    {
        var request = Request();
        var reset = Now.AddHours(-1);
        var result = Decide(request with
        {
            RetryReset = new(request.Management!.Identity, reset),
            RetryHistory = [Attempt(request, reset.AddTicks(-1)), Attempt(request, reset), Attempt(request, reset.AddTicks(1))],
        });
        Assert.True(result.IsEligible);
        Assert.Equal(1, result.CountedAttempts);
    }

    [Fact]
    public void Unknown_history_and_future_or_conflicting_evidence_fail_closed()
    {
        var request = Request();
        Assert.Equal(RestorationEligibilityOutcome.RetryHistoryUnknown, Decide(request with { RetryHistoryComplete = false }).Outcome);
        Assert.Equal(RestorationEligibilityOutcome.RetryEvidenceInvalid,
            Decide(request with { RetryReset = new(request.Management!.Identity, Now.AddTicks(1)) }).Outcome);
        Assert.Equal(RestorationEligibilityOutcome.RetryEvidenceInvalid,
            Decide(request with { RetryHistory = [Attempt(request, Now.AddTicks(1))] }).Outcome);
        var one = Attempt(request, Now);
        Assert.Equal(RestorationEligibilityOutcome.RetryEvidenceInvalid,
            Decide(request with { RetryHistory = [one, one with { OccurredAt = Now.AddTicks(-1) }] }).Outcome);
        Assert.Equal(RestorationEligibilityOutcome.RetryEvidenceInvalid,
            Decide(request with { RetryHistory = [one with { AttemptId = Guid.Empty }] }).Outcome);
    }
}