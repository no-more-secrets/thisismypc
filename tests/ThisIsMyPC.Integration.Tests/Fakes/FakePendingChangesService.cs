using System.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.Fakes;

public sealed class FakePendingChangesService : IPendingChangesService
{
    public int PendingCount => 0;
    public IReadOnlyList<ChangeGroup> PendingGroups => [];
    public bool IsApplying => false;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Stage(ChangeDescriptor change) { }
    public void Stage(ChangeGroup group) { }
    public void Unstage(string groupId) { }
    public void DiscardAll() { }

    public Task<MutationResult> ApplyAllAsync(
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc)
        => Task.FromResult(new MutationResult { IsSuccess = true, Applied = [], RolledBack = [] });
}
