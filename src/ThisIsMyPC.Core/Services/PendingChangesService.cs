using System.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

public sealed class PendingChangesService : IPendingChangesService
{
    private readonly List<ChangeGroup> _pendingGroups = [];
    private readonly object _lock = new();
    private readonly IEnforcementExecutor? _enforcementExecutor;

    public PendingChangesService(IEnforcementExecutor? enforcementExecutor = null)
    {
        _enforcementExecutor = enforcementExecutor;
    }

    public int PendingCount
    {
        get { lock (_lock) return _pendingGroups.Count; }
    }

    public IReadOnlyList<ChangeGroup> PendingGroups
    {
        get { lock (_lock) return _pendingGroups.ToList().AsReadOnly(); }
    }

    public bool IsApplying { get; private set; }

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
            removed = index >= 0;
            if (removed)
                _pendingGroups.RemoveAt(index);
        }

        if (removed)
        {
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(PendingGroups));
        }
    }

    public void DiscardAll()
    {
        lock (_lock)
        {
            if (_pendingGroups.Count == 0)
                return;

            _pendingGroups.Clear();
        }

        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(PendingGroups));
    }

    /// <remarks>
    /// Callers should be aware that PropertyChanged notifications after awaits
    /// may fire on a thread pool thread (due to ConfigureAwait(false)).
    /// UI-bound consumers must marshal to the UI thread.
    /// </remarks>
    public async Task<MutationResult> ApplyAllAsync(
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc)
    {
        ArgumentNullException.ThrowIfNull(applyFunc);
        ArgumentNullException.ThrowIfNull(revertFunc);

        // Snapshot pending groups under lock to avoid mutation during iteration
        List<ChangeGroup> snapshot;
        lock (_lock)
        {
            snapshot = [.. _pendingGroups];
        }

        // An enforced change with no executor is a DI misconfiguration; fail before
        // any change is applied, not mid-batch.
        if (_enforcementExecutor is null)
        {
            var enforced = snapshot
                .SelectMany(g => g.Changes)
                .FirstOrDefault(c => c.Enforcement is not null);
            if (enforced is not null)
            {
                throw new InvalidOperationException(
                    $"Change '{enforced.SettingId}' requires enforcement but no IEnforcementExecutor is configured.");
            }
        }

        IsApplying = true;
        OnPropertyChanged(nameof(IsApplying));

        var allApplied = new List<ChangeDescriptor>();

        try
        {
        for (var gi = 0; gi < snapshot.Count; gi++)
        {
            var group = snapshot[gi];
            var groupApplied = new List<ChangeDescriptor>();

            foreach (var change in group.Changes)
            {
                // Enforcement != null routes through the executor; null goes directly to
                // the module. No other heuristics (architecture.md L913/L973). The executor
                // is guaranteed non-null here by the pre-validation above.
                var result = change.Enforcement is not null
                    ? ToOperationResult(
                        await _enforcementExecutor!.ExecuteAsync(change, applyFunc).ConfigureAwait(false),
                        "Enforcement execution failed")
                    : await applyFunc(change).ConfigureAwait(false);

                if (result.IsSuccess)
                {
                    groupApplied.Add(change);
                }
                else
                {
                    // Rollback this group's applied changes in reverse order.
                    // The revert delegate contract (established by ChangeHistoryService undo)
                    // is "apply the descriptor's AfterValue"; so rollback must hand it a
                    // Before/After-SWAPPED descriptor, or modules whose RevertChangeAsync
                    // delegates to ApplyChangeAsync would re-apply the failed group's values.
                    var rolledBack = new List<ChangeDescriptor>();
                    for (var i = groupApplied.Count - 1; i >= 0; i--)
                    {
                        var original = groupApplied[i];
                        var swapped = original with
                        {
                            BeforeValue = original.AfterValue ?? string.Empty,
                            AfterValue = original.BeforeValue,
                            BeforeDisplay = original.AfterDisplay ?? string.Empty,
                            AfterDisplay = original.BeforeDisplay,
                        };

                        // Mirrors the apply routing exactly; an enforced change must never
                        // silently degrade to a bare revert (companion services/tasks/GPCache
                        // would stay mutated).
                        var rollbackResult = swapped.Enforcement is not null
                            ? ToOperationResult(
                                await _enforcementExecutor!.RevertAsync(swapped, revertFunc).ConfigureAwait(false),
                                "Enforcement revert failed")
                            : await revertFunc(swapped).ConfigureAwait(false);
                        if (rollbackResult.IsSuccess)
                        {
                            rolledBack.Add(groupApplied[i]);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"Rollback failed for '{groupApplied[i].SettingId}': {rollbackResult.ErrorMessage}");
                        }
                    }

                    // Remove successfully applied groups BY IDENTITY so pending state is
                    // consistent; index-based removal races with Stage/Unstage from the
                    // UI thread during the awaits above.
                    var appliedGroupIds = snapshot.Take(gi).Select(g => g.GroupId).ToHashSet();
                    lock (_lock)
                    {
                        _pendingGroups.RemoveAll(g => appliedGroupIds.Contains(g.GroupId));
                    }

                    OnPropertyChanged(nameof(PendingCount));
                    OnPropertyChanged(nameof(PendingGroups));

                    var failureRestarts = allApplied
                        .Select(c => c.RestartRequirement)
                        .Where(r => r != Changes.RestartRequirement.None)
                        .Distinct()
                        .ToList();

                    return new MutationResult
                    {
                        IsSuccess = false,
                        Applied = allApplied.AsReadOnly(),
                        Failed = change,
                        RolledBack = rolledBack.AsReadOnly(),
                        ErrorMessage = result.ErrorMessage,
                        ErrorCategory = result.ErrorCategory,
                        RequiredRestarts = failureRestarts,
                    };
                }
            }

            allApplied.AddRange(groupApplied);
        }

        // Remove only the groups we actually applied; a group staged while the batch
        // was running must survive.
        var snapshotIds = snapshot.Select(g => g.GroupId).ToHashSet();
        lock (_lock)
        {
            _pendingGroups.RemoveAll(g => snapshotIds.Contains(g.GroupId));
        }

        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(PendingGroups));

        var requiredRestarts = allApplied
            .Select(c => c.RestartRequirement)
            .Where(r => r != Changes.RestartRequirement.None)
            .Distinct()
            .ToList();

        return new MutationResult
        {
            IsSuccess = true,
            Applied = allApplied.AsReadOnly(),
            RolledBack = [],
            RequiredRestarts = requiredRestarts,
        };
        }
        finally
        {
            IsApplying = false;
            OnPropertyChanged(nameof(IsApplying));
        }
    }

    private static OperationResult<bool> ToOperationResult(
        Enforcement.EnforcementResult enforcement, string fallbackMessage) =>
        enforcement.IsSuccess
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure(
                enforcement.ErrorMessage ?? fallbackMessage,
                enforcement.ErrorCategory ?? Results.ErrorCategory.ServiceUnavailable);

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
