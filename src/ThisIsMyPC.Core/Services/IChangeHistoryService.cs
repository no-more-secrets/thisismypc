using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

public interface IChangeHistoryService
{
    Task InitializeAsync();
    Task RecordChangesAsync(MutationResult result);
    Task<IReadOnlyList<ChangeHistoryEntry>> GetHistoryAsync(int? limit = null, int? offset = null);
    Task<IReadOnlyList<ChangeHistoryEntry>> GetRecentGroupedAsync(int groupLimit = 50);
    Task<int> GetGroupCountAsync();
    Task<OperationResult<bool>> RevertChangeAsync(long historyId, Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc);
    Task<OperationResult<bool>> RedoChangeAsync(long historyId, Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc);
    Task<int> GetEntryCountAsync();
    Task ClearHistoryAsync();
}
