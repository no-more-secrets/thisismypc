using System.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.Fakes;

/// <summary>
/// A queue that never writes. <see cref="ApplyAllAsync"/> returns
/// <see cref="ApplyResult"/> as-is (default: success with nothing applied) and
/// clears the staged groups, so a test can hand the view model any result shape
/// the real service can produce, including ones with <c>Failed</c> null.
/// It does not override the token overload, so a cancellable token throws, as
/// the interface documents.
/// </summary>
public sealed class FakePendingChangesService : IPendingChangesService
{
    private readonly List<ChangeGroup> _groups = [];

    // Mirrors PendingChangesService: the count is of groups, not descriptors.
    public int PendingCount => _groups.Count;
    public IReadOnlyList<ChangeGroup> PendingGroups => _groups;
    public bool IsApplying => false;

    /// <summary>What the next ApplyAllAsync returns.</summary>
    public MutationResult ApplyResult { get; set; } = new() { IsSuccess = true, Applied = [], RolledBack = [] };

    /// <summary>How many times ApplyAllAsync ran.</summary>
    public int ApplyCalls { get; private set; }

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
    {
        ApplyCalls++;
        _groups.Clear();
        RaiseChanged();
        return Task.FromResult(ApplyResult);
    }

    private void RaiseChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingGroups)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingCount)));
    }
}
