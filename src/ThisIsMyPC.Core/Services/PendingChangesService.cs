using System.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

public sealed class PendingChangesService : IPendingChangesService
{
    private readonly List<ChangeGroup> _pendingGroups = [];

    public int PendingCount => _pendingGroups.Count;

    public IReadOnlyList<ChangeGroup> PendingGroups => _pendingGroups.AsReadOnly();

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

        _pendingGroups.Add(group);
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(PendingGroups));
    }

    public void Unstage(string groupId)
    {
        ArgumentNullException.ThrowIfNull(groupId);

        var index = _pendingGroups.FindIndex(g => g.GroupId == groupId);
        if (index >= 0)
        {
            _pendingGroups.RemoveAt(index);
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(PendingGroups));
        }
    }

    public void DiscardAll()
    {
        if (_pendingGroups.Count == 0)
            return;

        _pendingGroups.Clear();
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

        var allApplied = new List<ChangeDescriptor>();

        for (var gi = 0; gi < _pendingGroups.Count; gi++)
        {
            var group = _pendingGroups[gi];
            var groupApplied = new List<ChangeDescriptor>();

            foreach (var change in group.Changes)
            {
                var result = await applyFunc(change).ConfigureAwait(false);

                if (result.IsSuccess)
                {
                    groupApplied.Add(change);
                }
                else
                {
                    // Rollback this group's applied changes in reverse order
                    var rolledBack = new List<ChangeDescriptor>();
                    for (var i = groupApplied.Count - 1; i >= 0; i--)
                    {
                        await revertFunc(groupApplied[i]).ConfigureAwait(false);
                        rolledBack.Add(groupApplied[i]);
                    }

                    // Remove successfully applied groups so pending state is consistent
                    if (gi > 0)
                    {
                        _pendingGroups.RemoveRange(0, gi);
                    }

                    OnPropertyChanged(nameof(PendingCount));
                    OnPropertyChanged(nameof(PendingGroups));

                    return new MutationResult
                    {
                        IsSuccess = false,
                        Applied = allApplied.AsReadOnly(),
                        Failed = change,
                        RolledBack = rolledBack.AsReadOnly(),
                        ErrorMessage = result.ErrorMessage,
                        ErrorCategory = result.ErrorCategory
                    };
                }
            }

            allApplied.AddRange(groupApplied);
        }

        _pendingGroups.Clear();
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

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
