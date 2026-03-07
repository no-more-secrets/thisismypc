using System.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

public interface IPendingChangesService : INotifyPropertyChanged
{
    int PendingCount { get; }
    IReadOnlyList<ChangeGroup> PendingGroups { get; }

    void Stage(ChangeDescriptor change);
    void Stage(ChangeGroup group);
    void Unstage(string groupId);
    void DiscardAll();

    Task<MutationResult> ApplyAllAsync(
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc);
}
