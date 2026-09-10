using Microsoft.Extensions.Hosting;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Data;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Consent;
using ThisIsMyPC.Core.Drift.Eligibility;
using ThisIsMyPC.Core.Drift.Journal;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Coordination;
using ThisIsMyPC.Interop.Win32.Drift;
using ThisIsMyPC.Interop.Win32.Drift.Consent;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Service;

/// <summary>Production bounded scans. Every operation creates and retains its own trusted native storage scope.</summary>
public sealed class NativeRestorationService(IRegistryService registry) : BackgroundService, IRestorationServiceController
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly MachineConsentStore _consent = new();
    private readonly NativeRestorationEvidence _evidence = new();
    private RestorationStatusResponse _status = new() { Detail = "Waiting for a fresh restoration scan." };

    public RestorationStatusResponse GetStatus()
    {
        var status = Volatile.Read(ref _status);
        var consent = _consent.Read();
        if (!consent.IsGranted)
            return status with { State = consent.Status is MachineConsentStatus.Loaded or MachineConsentStatus.Missing
                ? RestorationServiceState.Paused : RestorationServiceState.Unavailable, ConsentGranted = false,
                Detail = consent.Status is MachineConsentStatus.Loaded or MachineConsentStatus.Missing
                    ? "Restoration is paused." : consent.Detail };
        return status with { ConsentGranted = true };
    }

    public Task<RestorationStatusResponse> EnableAsync(CancellationToken token = default) => ExecuteAsync(true, token);

    public async Task<RestorationStatusResponse> PauseAsync(CancellationToken token = default)
    {
        // Opt-out does not depend on journal, database, baseline, or a recovered lease.
        var leases = new NamedMutexMutationLeaseProvider(MutationLeaseNames.Production, "owner-mode-pause");
        var acquired = await leases.AcquireAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
        if (!acquired.IsAcquired) return Publish(RestorationServiceState.Conflict, "An active operation prevents pause.");
        await using var lease = acquired.Lease!;
        token.ThrowIfCancellationRequested();
        var off = _consent.SetEnabled(false, lease);
        return off.IsSuccess && off.State.Status == MachineConsentStatus.Loaded && !off.State.Enabled
            ? Publish(RestorationServiceState.Paused, "Restoration is paused.")
            : Publish(RestorationServiceState.Unavailable, off.State.Detail);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ExecuteAsync(false, stoppingToken).ConfigureAwait(false);
            await Task.Delay(RestorationLoop.DefaultInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task<RestorationStatusResponse> ExecuteAsync(bool enable, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!enable && !_consent.Read().IsGranted) return GetStatus();
            using var session = new NativeRestorationSession(registry);
            var prepared = await session.Coordinator.RunAsync(TimeSpan.FromSeconds(5), async (lease, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                await new RestorationJournalImporter(session.History).ImportAsync(session.Journal, lease).ConfigureAwait(false);
                var journal = session.Journal.Read(lease);
                if (!journal.IsHealthy || journal.Attempts.Any(a => a.Outcome is null || a.DiagnosticOnly ||
                    a.Outcome.Kind is JournalOutcomeKind.Uncertain or JournalOutcomeKind.RecoveryObservation))
                    return "Interrupted or uncertain restoration requires review before enabling.";
                var entries = session.Baseline.Read(lease);
                if (entries.Count == 0) return "Choose and apply a supported setting before enabling Owner Mode.";
                var evidenceItems = new List<RestorationLoopEvidence>();
                foreach (var entry in entries)
                {
                    var target = RestorationCatalog.Default.Targets.Single(t => t.ModuleId == entry.ModuleId && t.SettingId == entry.SettingId);
                    var evidence = _evidence.Read(new(target.ModuleId, target.SettingId, target.KeyPath, target.ValueName, session.Baseline.PrimaryUserSid));
                    evidenceItems.Add(evidence);
                }
                if (ReadinessFailure(evidenceItems) is { } notReady) return notReady;
                if (enable)
                {
                    var saved = session.Consent.SetEnabled(true, lease);
                    if (!saved.IsSuccess || !saved.State.IsGranted) return saved.State.Detail;
                }
                return (string?)null;
            }, token).ConfigureAwait(false);
            if (!prepared.OperationRan) return Publish(RestorationServiceState.Conflict, "Recovery or another operation prevents restoration.");
            if (prepared.Value is { } failure) return Publish(RestorationServiceState.Unavailable, failure);
            if (enable) return Publish(RestorationServiceState.Enabled, "Restoration is enabled for eligible saved settings. Managed settings stay excluded.");
            var state = RestorationServiceState.Enabled;
            var detail = "Protected settings match your choices.";
            var excluded = 0;
            var checkedTargets = 0;
            foreach (var target in RestorationCatalog.Default.Targets)
            {
                var loop = new RestorationLoop(session.Coordinator, session.Baseline, target, session.Consent,
                    session.Journal, new(session.History), registry, _evidence.Read, TimeProvider.System);
                var result = await loop.ScanAsync(token).ConfigureAwait(false);
                if (result.Status == RestorationScanStatus.BaselineMissing) continue;
                if (result.Status == RestorationScanStatus.Ineligible) { excluded++; continue; }
                if (result.Status is RestorationScanStatus.AlreadyMatches or RestorationScanStatus.Restored)
                {
                    checkedTargets++;
                    if (result.Status == RestorationScanStatus.Restored) detail = "Protected settings were restored.";
                    continue;
                }
                state = result.Status == RestorationScanStatus.ConsentOff ? RestorationServiceState.Paused : RestorationServiceState.Conflict;
                detail = result.Detail;
                break;
            }
            if (state == RestorationServiceState.Enabled && checkedTargets == 0)
            {
                state = RestorationServiceState.Unavailable;
                detail = "No saved settings are currently eligible for restoration.";
            }
            if (excluded > 0) detail += $" {excluded} saved settings are excluded by current eligibility checks.";
            return Publish(state, detail);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Publish(RestorationServiceState.Unavailable, "Restoration is unavailable: " + ex.Message); }
        finally { _gate.Release(); }
    }

    public static string? ReadinessFailure(IReadOnlyList<RestorationLoopEvidence> evidence)
    {
        if (evidence.Any(e => e.Profile?.State != RestorationProfileState.SupportedAndLoaded))
            return "The saved owner's profile is unavailable.";
        return evidence.Any(e => e.Management?.State == RestorationManagementState.Unmanaged)
            ? null : "No saved settings have unmanaged evidence. Existing policies may already control them.";
    }

    public async Task<IReadOnlyList<ChangeHistoryEntry>> GetHistoryAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var session = new NativeRestorationSession(registry);
            var result = await session.Coordinator.RunAsync(TimeSpan.FromSeconds(5), async (lease, _) =>
            {
                await new RestorationJournalImporter(session.History).ImportAsync(session.Journal, lease).ConfigureAwait(false);
                return (IReadOnlyList<ChangeHistoryEntry>)(await session.History.GetAllAsync(100).ConfigureAwait(false))
                    .Where(e => e.OwnerAttemptId is not null).ToArray();
            }, token).ConfigureAwait(false);
            if (!result.OperationRan) throw new IOException("Restoration history is unavailable during recovery.");
            return result.Value!;
        }
        finally { _gate.Release(); }
    }

    private RestorationStatusResponse Publish(RestorationServiceState state, string detail)
    {
        var result = new RestorationStatusResponse { State = state, Detail = detail,
            ConsentGranted = _consent.Read().IsGranted, LastScanUtc = DateTimeOffset.UtcNow };
        Volatile.Write(ref _status, result);
        return result;
    }
}
