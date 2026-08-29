using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.Fakes;

internal sealed class FakeChangeHistoryService : IChangeHistoryService
{
    public List<MutationResult> RecordedResults { get; } = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task RecordChangesAsync(MutationResult result)
    {
        RecordedResults.Add(result);
        return Task.CompletedTask;
    }

    public List<ChangeHistoryEntry> RecordedDriftEntries { get; } = [];

    public Task RecordDriftEventsAsync(IReadOnlyList<ChangeHistoryEntry> driftEntries)
    {
        RecordedDriftEntries.AddRange(driftEntries);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ChangeHistoryEntry>> GetHistoryAsync(int? limit = null, int? offset = null)
        => Task.FromResult<IReadOnlyList<ChangeHistoryEntry>>([]);

    public Task<IReadOnlyList<ChangeHistoryEntry>> GetRecentGroupedAsync(int groupLimit = 50)
        => Task.FromResult<IReadOnlyList<ChangeHistoryEntry>>([]);

    public Task<int> GetGroupCountAsync() => Task.FromResult(0);

    public Task<OperationResult<bool>> RevertChangeAsync(long historyId, Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc)
        => Task.FromResult(OperationResult<bool>.Success(true));

    public Task<OperationResult<bool>> RedoChangeAsync(long historyId, Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc)
        => Task.FromResult(OperationResult<bool>.Success(true));

    public Task<int> GetEntryCountAsync() => Task.FromResult(0);

    public Task ClearHistoryAsync() => Task.CompletedTask;
}

internal sealed class FakeChangeHistoryServiceWithEntries : IChangeHistoryService
{
    private readonly IReadOnlyList<ChangeHistoryEntry> _entries;
    private readonly OperationResult<bool>? _revertResult;
    private readonly OperationResult<bool>? _redoResult;

    public FakeChangeHistoryServiceWithEntries(
        IReadOnlyList<ChangeHistoryEntry> entries,
        OperationResult<bool>? revertResult = null,
        OperationResult<bool>? redoResult = null)
    {
        _entries = entries;
        _revertResult = revertResult;
        _redoResult = redoResult;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task RecordChangesAsync(MutationResult result) => Task.CompletedTask;

    public Task RecordDriftEventsAsync(IReadOnlyList<ChangeHistoryEntry> driftEntries) => Task.CompletedTask;

    public Task<IReadOnlyList<ChangeHistoryEntry>> GetHistoryAsync(int? limit = null, int? offset = null)
        => Task.FromResult(_entries);

    public Task<IReadOnlyList<ChangeHistoryEntry>> GetRecentGroupedAsync(int groupLimit = 50)
        => Task.FromResult(_entries);

    public Task<int> GetGroupCountAsync()
    {
        var count = _entries.Select(e => e.GroupId ?? e.Id.ToString()).Distinct().Count();
        return Task.FromResult(count);
    }

    public async Task<OperationResult<bool>> RevertChangeAsync(long historyId, Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc)
    {
        if (_revertResult is not null)
            return _revertResult;

        var entry = _entries.FirstOrDefault(e => e.Id == historyId);
        if (entry is null)
            return OperationResult<bool>.Failure("Not found", ErrorCategory.NotFound);

        var descriptor = new ChangeDescriptor
        {
            ModuleId = entry.ModuleId,
            SettingId = entry.SettingId,
            DisplayName = entry.DisplayName,
            SystemLocation = entry.SystemLocation,
            BeforeValue = entry.AfterValue ?? entry.BeforeValue!,
            AfterValue = entry.BeforeValue,
            BeforeDisplay = entry.AfterDisplay ?? entry.BeforeDisplay!,
            AfterDisplay = entry.BeforeDisplay,
            ValueType = entry.ValueType,
            Category = entry.Category,
        };

        return await revertFunc(descriptor).ConfigureAwait(false);
    }

    public async Task<OperationResult<bool>> RedoChangeAsync(long historyId, Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc)
    {
        if (_redoResult is not null)
            return _redoResult;

        var entry = _entries.FirstOrDefault(e => e.Id == historyId);
        if (entry is null)
            return OperationResult<bool>.Failure("Not found", ErrorCategory.NotFound);

        var descriptor = new ChangeDescriptor
        {
            ModuleId = entry.ModuleId,
            SettingId = entry.SettingId,
            DisplayName = entry.DisplayName,
            SystemLocation = entry.SystemLocation,
            BeforeValue = entry.BeforeValue!,
            AfterValue = entry.AfterValue,
            BeforeDisplay = entry.BeforeDisplay!,
            AfterDisplay = entry.AfterDisplay,
            ValueType = entry.ValueType,
            Category = entry.Category,
        };

        return await applyFunc(descriptor).ConfigureAwait(false);
    }

    public Task<int> GetEntryCountAsync() => Task.FromResult(_entries.Count);

    public Task ClearHistoryAsync() => Task.CompletedTask;
}
