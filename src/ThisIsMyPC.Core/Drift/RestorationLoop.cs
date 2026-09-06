using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift.Baseline;
using ThisIsMyPC.Core.Drift.Consent;
using ThisIsMyPC.Core.Drift.Eligibility;
using ThisIsMyPC.Core.Drift.Journal;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Drift;

/// <summary>Fresh trusted evidence for the bound account and exact catalog target.</summary>
public sealed record RestorationLoopEvidence(RestorationProfileEvidence? Profile,
    RestorationManagementEvidence? Management);

public enum RestorationScanStatus
{
    Restored, AlreadyMatches, ConsentOff, BaselineMissing, Ineligible, ReadFailed,
    UnsupportedObservation, RecoveryRequired, CoordinationBlocked, AttemptLimitReached,
    WriteFailed, Uncertain,
}

/// <summary>A scan outcome; exceptions from durable storage remain failures, never successful scans.</summary>
public sealed record RestorationScanResult(RestorationScanStatus Status, string Detail, Guid? AttemptId = null);

/// <summary>
/// One explicitly supplied catalog target. No production registration or native evidence provider.
/// Every scan acquires once and holds through consent, baseline, writes, journal and history import.
/// </summary>
public sealed class RestorationLoop
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);
    private readonly MutationCoordinator _coordinator;
    private readonly SingleOwnerBaselineStore _baseline;
    private readonly RestorationTarget _target;
    private readonly IMachineConsentStore _consent;
    private readonly RestorationJournal _journal;
    private readonly RestorationJournalImporter _importer;
    private readonly IRegistryService _registry;
    private readonly Func<RestorationIdentity, RestorationLoopEvidence> _evidence;
    private readonly TimeProvider _time;
    private readonly RestorationEligibilityPolicy _eligibility;

    public RestorationLoop(MutationCoordinator coordinator, SingleOwnerBaselineStore baseline,
        RestorationTarget target, IMachineConsentStore consent, RestorationJournal journal,
        RestorationJournalImporter importer, IRegistryService registry,
        Func<RestorationIdentity, RestorationLoopEvidence> evidence, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(consent);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(time);
        if (!ReferenceEquals(journal.TimeProvider, time))
            throw new ArgumentException("Journal and restoration must share one clock.", nameof(time));
        if (!ReferenceEquals(RestorationCatalog.Default.FindByLocation(target.KeyPath, target.ValueName), target))
            throw new ArgumentException("Only a shipped catalog target can be scanned.", nameof(target));
        _coordinator = coordinator; _baseline = baseline; _target = target; _consent = consent;
        _journal = journal; _importer = importer; _registry = registry; _evidence = evidence;
        _time = time; _eligibility = new(time);
    }

    /// <summary>Default cadence uses injected time. Tests supply ticks and never wait on wall time.</summary>
    public async Task RunAsync(Func<RestorationScanResult, Task> report,
        Func<CancellationToken, Task<bool>>? nextTick = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        while (!cancellationToken.IsCancellationRequested)
        {
            await report(await ScanAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            if (nextTick is not null)
            {
                if (!await nextTick(cancellationToken).ConfigureAwait(false)) return;
            }
            else await Task.Delay(DefaultInterval, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<RestorationScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        var run = await _coordinator.RunAsync(TimeSpan.FromSeconds(5), ScanHeldAsync, cancellationToken).ConfigureAwait(false);
        return run.OperationRan ? run.Value! : new(RestorationScanStatus.CoordinationBlocked,
            "Mutation acquisition or recovery did not authorize a scan.");
    }

    private async Task<RestorationScanResult> ScanHeldAsync(IMutationLease lease, CancellationToken cancellationToken)
    {
        if (!_consent.Read().IsGranted) return new(RestorationScanStatus.ConsentOff, "Owner Mode consent is off or unavailable.");
        var entry = _baseline.Read(lease).SingleOrDefault(e => e.ModuleId == _target.ModuleId && e.SettingId == _target.SettingId);
        if (entry is null) return new(RestorationScanStatus.BaselineMissing, "No chosen value is saved for this target.");
        var snapshot = _journal.Read(lease);
        if (!snapshot.IsHealthy)
            return new(RestorationScanStatus.RecoveryRequired, "Journal faults require reconciliation before restoration.");
        // Completed diagnostic outcomes also need history, even while restoration remains blocked.
        // The importer skips unmatched intents and never promotes them to successful outcomes.
        await _importer.ImportAsync(_journal, lease).ConfigureAwait(false);
        if (snapshot.Attempts.Any(a => a.Outcome is null || a.DiagnosticOnly ||
                a.Outcome.Kind is JournalOutcomeKind.Uncertain or JournalOutcomeKind.RecoveryObservation))
            return new(RestorationScanStatus.RecoveryRequired, "Journal uncertainty requires reconciliation before restoration.");
        var drift = new DriftBaselineEntry
        {
            ModuleId = entry.ModuleId, SettingId = entry.SettingId, DisplayName = _target.DisplayName,
            SystemLocation = entry.CanonicalLocation, ExpectedValue = entry.Expected.Data,
            UpdatedAtUtc = entry.UpdatedAtUtc, ValueType = _target.ValueType,
        };
        var identity = new RestorationIdentity(_target.ModuleId, _target.SettingId, _target.KeyPath,
            _target.ValueName, _baseline.PrimaryUserSid);
        var evidence = _evidence(identity);
        var attempts = snapshot.Attempts.Where(a => a.Intent.UserSid == identity.UserSid &&
            a.Intent.ModuleId == identity.ModuleId && a.Intent.SettingId == identity.SettingId &&
            a.Intent.CanonicalLocation == entry.CanonicalLocation).ToArray();
        var decision = _eligibility.Evaluate(new()
        {
            Entry = drift, UserSid = _baseline.PrimaryUserSid, MachineConsentGranted = true,
            Profile = evidence.Profile, Management = evidence.Management, RetryHistoryComplete = true,
            RetryHistory = attempts.Where(a => a.Outcome?.Kind == JournalOutcomeKind.Failed)
                .Select(a => new RestorationRetryObservation(identity, a.Intent.AttemptId, a.Intent.CreatedAt)).ToArray(),
        });
        if (!decision.IsEligible) return new(RestorationScanStatus.Ineligible, decision.Detail);
        var candidate = decision.Candidate!;
        var observed = Read(candidate);
        if (!observed.IsSuccess) return new(RestorationScanStatus.ReadFailed, observed.ErrorMessage ?? "Registry read failed.");
        var prepared = RestorationBatchFactory.Prepare(candidate, observed.Value!);
        if (prepared.Outcome == RestorationPreparationOutcome.AlreadyMatches)
            return new(RestorationScanStatus.AlreadyMatches, prepared.Detail);
        if (!prepared.IsReady) return new(RestorationScanStatus.UnsupportedObservation, prepared.Detail);
        // Conservative initial-loop ceiling, separate from adverse-only eligibility history.
        // No claim is made that previous successes were failures or repeated reversions.
        var now = _time.GetUtcNow();
        if (attempts.Any(a => a.Intent.CreatedAt > now) ||
            attempts.Count(a => now - a.Intent.CreatedAt <= RestorationEligibilityPolicy.RetryWindow) >= RestorationEligibilityPolicy.AttemptLimit)
            return new(RestorationScanStatus.AttemptLimitReached, "Three attempts in seven days, or invalid attempt dates, prevent another write.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!_consent.Read().IsGranted) return new(RestorationScanStatus.ConsentOff, "Consent was withdrawn before restoration.");
        var id = Guid.NewGuid();
        var permit = _journal.Begin(id, candidate, observed.Value!, lease).Permit
            ?? throw new InvalidOperationException("A new durable intent did not grant a permit.");
        var pending = PendingChangesService.Create(new ReversibleChangeExecutor());
        pending.Stage(RestorationBatchFactory.CreateGroup([prepared]));
        // Once intent is durable, finish bookkeeping even if the caller cancels.
        var mutation = await pending.ApplyAllAsync(
            _ => Write(candidate, candidate.DesiredValue, lease),
            _ => Write(candidate, observed.Value!.Value!, lease), CancellationToken.None).ConfigureAwait(false);
        var after = Read(candidate);
        var applied = mutation.IsSuccess && after.IsSuccess && after.Value!.Matches(candidate.DesiredValue);
        var unchanged = !mutation.HasUncertainState && after.IsSuccess && after.Value!.Matches(observed.Value!.Value!);
        var kind = applied ? JournalOutcomeKind.Applied : unchanged ? JournalOutcomeKind.Failed : JournalOutcomeKind.Uncertain;
        var detail = applied ? "Restored and verified." : mutation.ErrorMessage ?? "Restoration did not produce a verified successful write.";
        if (detail.Length > 2048) detail = detail[..2048];
        _journal.Complete(permit, new(kind, after.IsSuccess ? after.Value!.Value : null, detail), lease);
        await _importer.ImportAsync(_journal, lease).ConfigureAwait(false);
        return new(applied ? RestorationScanStatus.Restored : unchanged ? RestorationScanStatus.WriteFailed : RestorationScanStatus.Uncertain, detail, id);
    }

    private OperationResult<RegistryValueSnapshot> Read(RestorationCandidate candidate)
        => RegistryValueSnapshot.FromRead(_registry.ReadValue(candidate.ResolvedKeyPath, candidate.Target.ValueName));

    private Task<OperationResult<bool>> Write(RestorationCandidate candidate, RegistryValueData value, IMutationLease lease)
    {
        if (!lease.CanWrite) throw new InvalidOperationException("Restoration requires the held recovered lease.");
        return Task.FromResult(_registry.WriteValue(candidate.ResolvedKeyPath, candidate.Target.ValueName, value));
    }
}
