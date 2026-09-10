using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Data;
using ThisIsMyPC.Core.Drift.Baseline;
using ThisIsMyPC.Core.Drift.Consent;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Drift;

/// <summary>Serializes deliberate writes and removes stale protected choices before any system mutation.</summary>
public sealed class DeliberateChangeCoordinator(MutationCoordinator coordinator, SingleOwnerBaselineStore baseline,
    IRegistryService registry, TimeProvider time) : IDisposable
{
    private readonly MutationCoordinator _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    private readonly SingleOwnerBaselineStore _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
    private readonly IRegistryService _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));

    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>Refuses new operations and cancels waits. An operation past preparation finishes its bookkeeping.</summary>
    public void StopAcceptingChanges() => _shutdown.Cancel();

    /// <summary>Releases the shutdown source after all operations have finished.</summary>
    public void Dispose() => _shutdown.Dispose();

    /// <summary>Holds one lease through the pending batch, rollback, history and protected-choice persistence.</summary>
    public Task<MutationResult> ApplyAsync(IPendingChangesService pending, IChangeHistoryService history,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> apply,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revert,
        Func<Func<Task<MutationResult>>, Task<MutationResult>>? dispatch = null,
        CancellationToken cancellationToken = default)
        => RunAsync((session, token) => dispatch is null
            ? ApplyHeldAsync(session, pending, history, apply, revert, token)
            : dispatch(() => ApplyHeldAsync(session, pending, history, apply, revert, token)), cancellationToken);

    private static async Task<MutationResult> ApplyHeldAsync(DeliberateChangeSession session,
        IPendingChangesService pending, IChangeHistoryService history,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> apply,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revert, CancellationToken token)
    {
        var result = await pending.ApplyAllAsync(apply, revert, session.Prepare, token).ConfigureAwait(false);
        if (result.Applied.Count > 0)
        {
            await history.RecordChangesAsync(result).ConfigureAwait(false);
            session.RecordApplied(result.Applied);
        }
        return result;
    }

    /// <summary>Acquires once. The callback owns apply, rollback, history and chosen-value persistence.</summary>
    public async Task<T> RunAsync<T>(Func<DeliberateChangeSession, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var run = await _coordinator.RunAsync(TimeSpan.FromSeconds(30),
            (lease, token) => operation(new(_baseline, _registry, _time, lease, token), token), linked.Token).ConfigureAwait(false);
        if (!run.OperationRan)
        {
            linked.Token.ThrowIfCancellationRequested();
            throw new InvalidOperationException(run.Recovery?.ErrorMessage ?? run.Acquisition.ErrorMessage
                ?? "The mutation lease or recovery did not authorize this change.");
        }
        return run.Value!;
    }
}

/// <summary>A single held operation. Never retain this session or acquire another lease inside its delegates.</summary>
public sealed class DeliberateChangeSession
{
    private readonly SingleOwnerBaselineStore _baseline;
    private readonly IRegistryService _registry;
    private readonly TimeProvider _time;
    private readonly IMutationLease _lease;
    private readonly Dictionary<string, ChangeDescriptor> _prepared = new(StringComparer.OrdinalIgnoreCase);
    private bool _started;
    private readonly CancellationToken _cancellationToken;

    internal DeliberateChangeSession(SingleOwnerBaselineStore baseline, IRegistryService registry,
        TimeProvider time, IMutationLease lease, CancellationToken cancellationToken)
    {
        _baseline = baseline; _registry = registry; _time = time; _lease = lease;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Revalidates catalog before-values after waiting, then durably removes every affected protected choice.</summary>
    public void Prepare(IReadOnlyList<ChangeDescriptor> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        _cancellationToken.ThrowIfCancellationRequested();
        CheckHeld();
        if (_started) throw new InvalidOperationException("A deliberate mutation session can prepare only once.");
        _started = true;
        foreach (var change in changes)
        {
            var target = FindTarget(change.SystemLocation);
            if (target is null) continue;
            var location = target.KeyPath + "\\" + target.ValueName;
            if (!_prepared.TryAdd(location, change))
                throw new InvalidOperationException("Multiple staged changes address the same protected setting. Stage it again.");
            var read = RegistryValueSnapshot.FromRead(_registry.ReadValue(ResolvedPath(target), target.ValueName));
            var absent = change.ValueType == ChangeValueType.Registry_DWord && change.BeforeValue.Length == 0;
            var matches = read.IsSuccess && (absent ? !read.Value!.IsPresent :
                change.ValueType == Changes.ChangeValueType.Registry_DWord &&
                read.Value!.Matches(new RegistryValueData(RegistryValueDataKind.DWord, change.BeforeValue)));
            if (!matches)
                throw new InvalidOperationException("A protected setting changed after staging, or could not be read. Discard and stage it again: " + change.DisplayName);
        }
        // Removal is durable before the first write. A crash cannot leave an old restoration choice behind.
        if (_prepared.Count > 0) _baseline.Remove(_prepared.Keys.ToArray(), _lease);
    }

    /// <summary>After history succeeds, saves only catalog choices verified against the live typed value.</summary>
    public void RecordApplied(IReadOnlyList<ChangeDescriptor> applied)
    {
        ArgumentNullException.ThrowIfNull(applied);
        CheckHeld();
        if (!_started) throw new InvalidOperationException("Prepare must run before recording deliberate changes.");
        var entries = new List<SingleOwnerBaselineEntry>();
        foreach (var change in applied)
        {
            var target = FindTarget(change.SystemLocation);
            if (target is null) continue;
            var location = target.KeyPath + "\\" + target.ValueName;
            if (!_prepared.TryGetValue(location, out var staged) || staged != change)
                throw new InvalidOperationException("An unprepared change cannot become a protected choice.");
            var validation = RestorationCatalog.Default.Validate(new DriftBaselineEntry
            {
                ModuleId = change.ModuleId, SettingId = change.SettingId, DisplayName = change.DisplayName,
                SystemLocation = location, ExpectedValue = change.AfterValue ?? string.Empty,
                ValueType = change.ValueType, EnforcementJson = EnforcementJson.Serialize(change.Enforcement),
                UpdatedAtUtc = _time.GetUtcNow(),
            }, _baseline.PrimaryUserSid);
            // Unsupported or absent choices stay unprotected rather than retaining the old choice.
            if (!validation.IsAccepted) continue;
            var read = RegistryValueSnapshot.FromRead(_registry.ReadValue(ResolvedPath(target), target.ValueName));
            if (!read.IsSuccess || !read.Value!.Matches(validation.Candidate!.DesiredValue))
                throw new InvalidOperationException("The applied protected setting could not be verified: " + change.DisplayName);
            entries.Add(new(change.ModuleId, change.SettingId, location, validation.Candidate!.DesiredValue, _time.GetUtcNow()));
        }
        if (entries.Count > 0) _baseline.RecordApplied(entries, _lease);
    }

    /// <summary>Disarms an ambiguous baseline save before rollback while retaining the same mutation lease.</summary>
    public void DisableProtection(IMachineConsentStore consent)
    {
        ArgumentNullException.ThrowIfNull(consent);
        CheckHeld();
        if (!_started) throw new InvalidOperationException("Prepare must run before disabling protection.");
        if (_prepared.Count == 0) return;
        try
        {
            _baseline.Remove(_prepared.Keys.ToArray(), _lease);
        }
        catch (Exception removalError)
        {
            // An atomic replace may have committed before reporting failure. Consent-off disarms that choice.
            var off = consent.SetEnabled(false, _lease);
            if (!off.IsSuccess || off.State.Status != MachineConsentStatus.Loaded || off.State.Enabled)
                throw new InvalidOperationException(
                    "The protected choice could not be removed and paused consent could not be saved.", removalError);
        }
    }

    private RestorationTarget? FindTarget(string location)
    {
        var normalized = location.Replace("HKEY_CURRENT_USER\\", "HKCU\\", StringComparison.OrdinalIgnoreCase)
            .Replace("HKEY_USERS\\", "HKU\\", StringComparison.OrdinalIgnoreCase);
        var boundPrefix = "HKU\\" + _baseline.PrimaryUserSid + "\\";
        if (normalized.StartsWith(boundPrefix, StringComparison.OrdinalIgnoreCase))
            normalized = "HKCU\\" + normalized[boundPrefix.Length..];
        var split = normalized.LastIndexOf('\\');
        return split <= 0 ? null : RestorationCatalog.Default.FindByLocation(normalized[..split], normalized[(split + 1)..]);
    }

    private string ResolvedPath(RestorationTarget target) => "HKU\\" + _baseline.PrimaryUserSid + "\\" + target.KeyPath[5..];
    private void CheckHeld()
    {
        if (!_lease.CanWrite) throw new InvalidOperationException("A held recovered lease is required for deliberate changes.");
    }
}
