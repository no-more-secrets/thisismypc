using System.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

/// <summary>
/// One staging queue of reversible change groups. Each instance owns its own
/// queue: the app's shared queue is one instance, and Owner Mode restoration
/// gets its own through <see cref="Create"/>, so a background restore never
/// mixes with what the person has staged. Every descriptor executes through
/// <see cref="IReversibleChangeExecutor"/>; this class owns group order,
/// mid-group rollback, cancellation, and queue bookkeeping, nothing about routing.
/// </summary>
public sealed class PendingChangesService : IPendingChangesService
{
    private readonly List<ChangeGroup> _pendingGroups = [];

    // Keyed by group instance, not id: a caller may stage a fresh group under the
    // same id after reading the live values, and that new group must not inherit
    // the old one's record. Only DiscardAll removes reconciliation entries.
    private readonly Dictionary<ChangeGroup, GroupReconciliation> _reconciliation =
        new(ReferenceEqualityComparer.Instance);

    private readonly object _lock = new();
    private readonly IReversibleChangeExecutor _executor;
    private bool _isApplying;

    public PendingChangesService(IEnforcementExecutor? enforcementExecutor = null)
        : this(new ReversibleChangeExecutor(enforcementExecutor))
    {
    }

    private PendingChangesService(IReversibleChangeExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    /// <summary>
    /// A queue that executes through an already-built executor, so two queues
    /// (the app's and restoration's) share exactly one routing path. A static
    /// factory rather than a second one-argument constructor: MS DI resolves
    /// <see cref="IPendingChangesService"/> by constructor and two satisfiable
    /// single-parameter constructors would be ambiguous.
    /// </summary>
    public static PendingChangesService Create(IReversibleChangeExecutor executor) => new(executor);

    public int PendingCount
    {
        get { lock (_lock) return _pendingGroups.Count; }
    }

    public IReadOnlyList<ChangeGroup> PendingGroups
    {
        get { lock (_lock) return _pendingGroups.ToList().AsReadOnly(); }
    }

    public bool IsApplying
    {
        get { lock (_lock) return _isApplying; }
    }

    public IReadOnlyList<GroupReconciliation> ReconciliationRequired
    {
        get
        {
            lock (_lock)
            {
                return _pendingGroups
                    .Where(_reconciliation.ContainsKey)
                    .Select(g => _reconciliation[g])
                    .ToList()
                    .AsReadOnly();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Stage(ChangeDescriptor change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.BeforeValue is null)
        {
            throw new ArgumentException("BeforeValue is required and cannot be null.", nameof(change));
        }

        var group = new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = change.DisplayName,
            Description = change.DisplayName,
            Changes = [change]
        };

        Stage(group);
    }

    public void Stage(ChangeGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        foreach (var change in group.Changes)
        {
            if (change.BeforeValue is null)
            {
                throw new ArgumentException(
                    $"BeforeValue is required and cannot be null for change '{change.SettingId}'.",
                    nameof(group));
            }
        }

        lock (_lock)
        {
            // One id, one staged group. A duplicate (same instance or another one
            // under the same id) would let Unstage remove one entry and its
            // reconciliation mark while the other stayed applyable, and would make
            // the review panel show one row for two batches of writes.
            if (_pendingGroups.Any(g => g.GroupId == group.GroupId))
            {
                throw new InvalidOperationException(
                    $"A group with id '{group.GroupId}' is already staged. Unstage it before staging another under that id.");
            }

            _pendingGroups.Add(group);
        }

        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(PendingGroups));
    }

    public void Unstage(string groupId)
    {
        ArgumentNullException.ThrowIfNull(groupId);

        bool removed;

        lock (_lock)
        {
            var index = _pendingGroups.FindIndex(g => g.GroupId == groupId);
            // Delayed page callbacks may replace a staged group after apply failed.
            // Keep the uncertain group until explicit discard triggers a fresh scan.
            if (index >= 0 && _reconciliation.ContainsKey(_pendingGroups[index]))
                return;

            removed = index >= 0;
            if (removed)
            {

                _pendingGroups.RemoveAt(index);
            }
        }

        if (removed)
        {
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(PendingGroups));

        }
    }

    public void DiscardAll()
    {
        bool clearedReconciliation;
        lock (_lock)
        {
            if (_pendingGroups.Count == 0)
                return;

            clearedReconciliation = _reconciliation.Count > 0;
            _pendingGroups.Clear();
            _reconciliation.Clear();
        }

        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(PendingGroups));
        if (clearedReconciliation)
            OnPropertyChanged(nameof(ReconciliationRequired));
    }

    public Task<MutationResult> ApplyAllAsync(
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc)
        => ApplyAllAsync(applyFunc, revertFunc, CancellationToken.None);

    /// <remarks>
    /// <para>
    /// The token is checked before each change starts and nowhere else. It is not
    /// passed to the executor or to either delegate, so an apply in flight always
    /// runs to completion and its result is honored; rollback runs with no token
    /// at all, so cleanup cannot be cut short by the cancellation that triggered
    /// it. A delegate that throws <see cref="OperationCanceledException"/> on its
    /// own is treated exactly like any other throw: the change is uncertain.
    /// </para>
    /// <para>
    /// Callers should be aware that PropertyChanged notifications after awaits
    /// may fire on a thread pool thread (due to ConfigureAwait(false)).
    /// UI-bound consumers must marshal to the UI thread.
    /// </para>
    /// </remarks>
    public Task<MutationResult> ApplyAllAsync(
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc,
        CancellationToken cancellationToken)
        => ApplyAllAsync(applyFunc, revertFunc, _ => { }, cancellationToken);

    /// <summary>Prepares the exact immutable batch snapshot before executing it.</summary>
    public async Task<MutationResult> ApplyAllAsync(
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc,
        Action<IReadOnlyList<ChangeDescriptor>> prepare,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(applyFunc);
        ArgumentNullException.ThrowIfNull(revertFunc);

        // Snapshot pending groups under lock to avoid mutation during iteration.
        // The same lock owns the one-batch-at-a-time rule: a second caller would
        // snapshot the still-staged groups and apply every one of them twice.
        List<ChangeGroup> snapshot;
        lock (_lock)
        {
            if (_isApplying)
                throw new InvalidOperationException("A change batch is already applying.");

            // A group left in an unknown state blocks the whole queue. Its before
            // values may be stale, so applying it again could record a wrong undo,
            // and applying around it would hide the problem. No writer runs.
            var blocked = _pendingGroups.FirstOrDefault(_reconciliation.ContainsKey);
            if (blocked is not null)
            {
                var record = _reconciliation[blocked];
                return new MutationResult
                {
                    IsSuccess = false,
                    FailureKind = MutationFailureKind.ReconciliationRequired,
                    Applied = [],
                    RolledBack = [],
                    Failed = record.Failed ?? record.Uncertain[0],
                    Uncertain = record.Uncertain,
                    RollbackFailures = record.RollbackFailures,
                    ErrorMessage =
                        $"'{blocked.DisplayName}' was left in an unknown state by an earlier apply. Discard it and stage it again before applying.",
                    ErrorCategory = ErrorCategory.ServiceUnavailable,
                };
            }

            snapshot = [.. _pendingGroups];

            // An enforced change with no executor is a DI misconfiguration; fail before
            // any change is applied, not mid-batch.
            var unroutable = snapshot
                .SelectMany(g => g.Changes)
                .FirstOrDefault(c => !_executor.CanExecute(c));
            if (unroutable is not null)
            {
                throw new InvalidOperationException(
                    $"Change '{unroutable.SettingId}' requires enforcement but no IEnforcementExecutor is configured.");
            }

            _isApplying = true;
        }

        OnPropertyChanged(nameof(IsApplying));

        // Groups that completed every change. They are committed: removed from the
        // queue on every exit path below so a retry never replays them. Tracked by
        // instance, not id: a caller may Unstage a group and stage a replacement
        // under the same id while the batch runs, and that replacement must stay.
        var completedGroups = new HashSet<ChangeGroup>(ReferenceEqualityComparer.Instance);
        var allApplied = new List<ChangeDescriptor>();

        try
        {
            prepare(snapshot.SelectMany(group => group.Changes).ToArray());
            foreach (var group in snapshot)
            {
                var groupApplied = new List<ChangeDescriptor>();

                foreach (var change in group.Changes)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return await StopAsync(
                            group,
                            MutationFailureKind.Cancelled,
                            failed: null,
                            errorMessage: "The batch was cancelled before every change was applied.",
                            errorCategory: null,
                            exception: null,
                            groupApplied, allApplied, completedGroups, revertFunc).ConfigureAwait(false);
                    }

                    OperationResult<bool> result;
                    try
                    {
                        // Routing (Enforcement != null through the enforcement executor, null
                        // directly to the module delegate) lives in the shared executor. No
                        // token goes down: the in-flight call is always awaited to its end.
                        result = await _executor.ApplyAsync(change, applyFunc).ConfigureAwait(false);
                    }
#pragma warning disable CA1031 // A throw mid-batch must still roll the group back and commit finished groups
                    catch (Exception ex)
#pragma warning restore CA1031
                    {
                        // The change may or may not have written. It is reported, never
                        // reverted (a blind revert of an enforced change could undo companion
                        // work that never happened), and its group stays staged.
                        return await StopAsync(
                            group,
                            MutationFailureKind.ChangeThrew,
                            failed: change,
                            errorMessage: $"'{change.DisplayName}' threw while applying: {ex.Message}",
                            errorCategory: ErrorCategory.ServiceUnavailable,
                            exception: ex,
                            groupApplied, allApplied, completedGroups, revertFunc).ConfigureAwait(false);
                    }

                    if (!result.IsSuccess)
                    {
                        // A failed result is the module's report, not evidence about the
                        // value: modules catch their own exceptions, and enforcement can
                        // fail after its primary write succeeded. The change is uncertain.
                        return await StopAsync(
                            group,
                            MutationFailureKind.ChangeFailed,
                            failed: change,
                            errorMessage: result.ErrorMessage,
                            errorCategory: result.ErrorCategory,
                            exception: null,
                            groupApplied, allApplied, completedGroups, revertFunc).ConfigureAwait(false);
                    }

                    groupApplied.Add(change);
                }

                allApplied.AddRange(groupApplied);
                completedGroups.Add(group);
            }

            RemoveGroups(completedGroups);

            return new MutationResult
            {
                IsSuccess = true,
                Applied = allApplied.AsReadOnly(),
                RolledBack = [],
                RequiredRestarts = RestartsOf(allApplied),
            };
        }
        finally
        {
            lock (_lock)
            {
                _isApplying = false;
            }

            OnPropertyChanged(nameof(IsApplying));
        }
    }

    /// <summary>
    /// Every non-success exit: roll back the current group's applied changes in
    /// reverse order, commit the groups that finished, mark the current group as
    /// needing reconciliation when any change's live value is unknown, and
    /// describe what happened. Runs with no cancellation token on purpose.
    /// </summary>
    private async Task<MutationResult> StopAsync(
        ChangeGroup group,
        MutationFailureKind kind,
        ChangeDescriptor? failed,
        string? errorMessage,
        ErrorCategory? errorCategory,
        Exception? exception,
        List<ChangeDescriptor> groupApplied,
        List<ChangeDescriptor> allApplied,
        HashSet<ChangeGroup> completedGroups,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc)
    {
        var rolledBack = new List<ChangeDescriptor>();
        var rollbackFailures = new List<RollbackFailure>();

        for (var i = groupApplied.Count - 1; i >= 0; i--)
        {
            var original = groupApplied[i];

            // The revert delegate contract (established by ChangeHistoryService undo)
            // is "apply the descriptor's AfterValue"; so rollback must hand it a
            // Before/After-SWAPPED descriptor, or modules whose RevertChangeAsync
            // delegates to ApplyChangeAsync would re-apply the failed group's values.
            var swapped = original with
            {
                BeforeValue = original.AfterValue ?? string.Empty,
                AfterValue = original.BeforeValue,
                BeforeDisplay = original.AfterDisplay ?? string.Empty,
                AfterDisplay = original.BeforeDisplay,
            };

            try
            {
                // Same routing as apply; an enforced change must never silently
                // degrade to a bare revert (companion services/tasks/GPCache would
                // stay mutated).
                var rollbackResult = await _executor.RevertAsync(swapped, revertFunc).ConfigureAwait(false);
                if (rollbackResult.IsSuccess)
                {
                    rolledBack.Add(original);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Rollback failed for '{original.SettingId}': {rollbackResult.ErrorMessage}");
                    rollbackFailures.Add(new RollbackFailure(original, rollbackResult.ErrorMessage, null));
                }
            }
#pragma warning disable CA1031 // One revert throwing must not abandon the rest of the cleanup
            catch (Exception ex)
#pragma warning restore CA1031
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Rollback threw for '{original.SettingId}': {ex.Message}");
                rollbackFailures.Add(new RollbackFailure(original, ex.Message, ex));
            }
        }

        // Unknown live values, in group order: the change the batch stopped at (a
        // failed result is not proof of no write) and every change not put back.
        var unresolved = new HashSet<ChangeDescriptor>(ReferenceEqualityComparer.Instance);
        if (failed is not null)
            unresolved.Add(failed);
        foreach (var failure in rollbackFailures)
            unresolved.Add(failure.Change);
        var uncertain = group.Changes.Where(unresolved.Contains).ToList().AsReadOnly();

        // The current group stays staged in every case. When any of its values is
        // unknown it is also marked, so the next batch refuses instead of writing
        // from before values that may no longer describe the machine.
        var marked = false;
        lock (_lock)
        {
            if (uncertain.Count > 0 && _pendingGroups.Any(g => ReferenceEquals(g, group)))
            {
                _reconciliation[group] = new GroupReconciliation
                {
                    Group = group,
                    Kind = kind,
                    Failed = failed,
                    Uncertain = uncertain,
                    RolledBack = rolledBack.AsReadOnly(),
                    RollbackFailures = rollbackFailures.AsReadOnly(),
                    ErrorMessage = errorMessage,
                    Exception = exception,
                    RecordedAt = DateTimeOffset.UtcNow,
                };
                marked = true;
            }
        }

        if (marked)
            OnPropertyChanged(nameof(ReconciliationRequired));

        RemoveGroups(completedGroups);

        return new MutationResult
        {
            IsSuccess = false,
            FailureKind = kind,
            Applied = allApplied.AsReadOnly(),
            Failed = failed,
            RolledBack = rolledBack.AsReadOnly(),
            RollbackFailures = rollbackFailures.AsReadOnly(),
            Uncertain = uncertain,
            ErrorMessage = errorMessage,
            ErrorCategory = errorCategory,
            Exception = exception,
            RequiredRestarts = RestartsOf(allApplied),
        };
    }

    /// <summary>
    /// Remove committed groups by exact instance so pending state is consistent:
    /// index-based removal races with Stage/Unstage from the UI thread during the
    /// awaits, and id-based removal would take a replacement staged under the
    /// same id after an Unstage mid-batch. A group staged while the batch ran
    /// must survive.
    /// </summary>
    private void RemoveGroups(HashSet<ChangeGroup> groups)
    {
        lock (_lock)
        {
            _pendingGroups.RemoveAll(groups.Contains);
        }

        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(PendingGroups));
    }

    private static List<RestartRequirement> RestartsOf(IEnumerable<ChangeDescriptor> applied) =>
        applied
            .Select(c => c.RestartRequirement)
            .Where(r => r != RestartRequirement.None)
            .Distinct()
            .ToList();

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
