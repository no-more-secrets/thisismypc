using System.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.Fakes;

public sealed class FakePendingChangesService : IPendingChangesService
{
    private readonly List<ChangeGroup> _groups = [];

    // Mirrors PendingChangesService: the count is of groups, not descriptors.
    public int PendingCount => _groups.Count;
    public IReadOnlyList<ChangeGroup> PendingGroups => _groups;
    public bool IsApplying => false;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Stage(ChangeDescriptor change)
        => Stage(new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = change.DisplayName,
            Description = change.DisplayName,
            Changes = [change],
        });

    public void Stage(ChangeGroup group)
    {
        _groups.Add(group);
        RaiseChanged();
    }

    public void Unstage(string groupId)
    {
        _groups.RemoveAll(g => g.GroupId == groupId);
        RaiseChanged();
    }

    public void DiscardAll()
    {
        _groups.Clear();
        RaiseChanged();
    }

    public Task<MutationResult> ApplyAllAsync(
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc)
        => Task.FromResult(new MutationResult { IsSuccess = true, Applied = [], RolledBack = [] });

    private void RaiseChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingGroups)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingCount)));
    }
}
