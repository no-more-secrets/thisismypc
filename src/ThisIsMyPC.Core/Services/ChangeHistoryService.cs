using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Data;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

public sealed class ChangeHistoryService : IChangeHistoryService
{
    private readonly ChangeHistoryRepository _repository;
    private readonly string _dbPath;

    public ChangeHistoryService(ChangeHistoryRepository repository, string? dbPath = null)
    {
        _repository = repository;
        _dbPath = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ThisIsMyPC",
            "history.db");
    }

    public async Task InitializeAsync()
    {
        await _repository.InitializeDatabaseAsync(_dbPath).ConfigureAwait(false);
    }

    public async Task RecordChangesAsync(MutationResult result)
    {
        if (!result.IsSuccess || result.Applied.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var groupId = Guid.NewGuid().ToString("N");

        foreach (var change in result.Applied)
        {
            var entry = new ChangeHistoryEntry
            {
                ModuleId = change.ModuleId,
                SettingId = change.SettingId,
                DisplayName = change.DisplayName,
                SystemLocation = change.SystemLocation,
                BeforeValue = change.BeforeValue,
                AfterValue = change.AfterValue,
                BeforeDisplay = change.BeforeDisplay,
                AfterDisplay = change.AfterDisplay,
                ValueType = change.ValueType,
                Category = change.Category,
                GroupId = groupId,
                AppliedAt = now,
            };

            await _repository.InsertAsync(entry).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<ChangeHistoryEntry>> GetHistoryAsync(int? limit = null, int? offset = null)
    {
        return await _repository.GetAllAsync(limit, offset).ConfigureAwait(false);
    }

    public async Task<OperationResult<bool>> RevertChangeAsync(
        long historyId,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc)
    {
        var entry = await _repository.GetByIdAsync(historyId).ConfigureAwait(false);

        if (entry is null)
        {
            return OperationResult<bool>.Failure(
                $"History entry {historyId} not found",
                ErrorCategory.NotFound);
        }

        if (entry.RevertedAt.HasValue)
        {
            return OperationResult<bool>.Failure(
                "This change has already been reverted",
                ErrorCategory.NotFound);
        }

        var revertDescriptor = new ChangeDescriptor
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

        var result = await revertFunc(revertDescriptor).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return OperationResult<bool>.Failure(
                result.ErrorMessage ?? "Revert operation failed",
                result.ErrorCategory ?? ErrorCategory.ServiceUnavailable);
        }

        var revertEntry = new ChangeHistoryEntry
        {
            ModuleId = entry.ModuleId,
            SettingId = entry.SettingId,
            DisplayName = entry.DisplayName,
            SystemLocation = entry.SystemLocation,
            BeforeValue = entry.AfterValue,
            AfterValue = entry.BeforeValue,
            BeforeDisplay = entry.AfterDisplay,
            AfterDisplay = entry.BeforeDisplay,
            ValueType = entry.ValueType,
            Category = entry.Category,
            GroupId = entry.GroupId,
            AppliedAt = DateTimeOffset.UtcNow,
        };

        var insertedRevert = await _repository.InsertAsync(revertEntry).ConfigureAwait(false);
        await _repository.UpdateRevertedAtAsync(historyId, DateTimeOffset.UtcNow, insertedRevert.Id)
            .ConfigureAwait(false);

        return OperationResult<bool>.Success(true);
    }

    public async Task<OperationResult<bool>> RedoChangeAsync(
        long historyId,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc)
    {
        var entry = await _repository.GetByIdAsync(historyId).ConfigureAwait(false);

        if (entry is null)
        {
            return OperationResult<bool>.Failure(
                $"History entry {historyId} not found",
                ErrorCategory.NotFound);
        }

        if (!entry.RevertedAt.HasValue)
        {
            return OperationResult<bool>.Failure(
                "This change has not been reverted and cannot be redone",
                ErrorCategory.NotFound);
        }

        var redoDescriptor = new ChangeDescriptor
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

        var result = await applyFunc(redoDescriptor).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return OperationResult<bool>.Failure(
                result.ErrorMessage ?? "Redo operation failed",
                result.ErrorCategory ?? ErrorCategory.ServiceUnavailable);
        }

        var redoEntry = new ChangeHistoryEntry
        {
            ModuleId = entry.ModuleId,
            SettingId = entry.SettingId,
            DisplayName = entry.DisplayName,
            SystemLocation = entry.SystemLocation,
            BeforeValue = entry.BeforeValue,
            AfterValue = entry.AfterValue,
            BeforeDisplay = entry.BeforeDisplay,
            AfterDisplay = entry.AfterDisplay,
            ValueType = entry.ValueType,
            Category = entry.Category,
            GroupId = entry.GroupId,
            AppliedAt = DateTimeOffset.UtcNow,
            RedoOfEntryId = historyId,
        };

        await _repository.InsertAsync(redoEntry).ConfigureAwait(false);
        await _repository.ClearRevertedAtAsync(historyId).ConfigureAwait(false);

        return OperationResult<bool>.Success(true);
    }

    public async Task<int> GetEntryCountAsync()
    {
        return await _repository.GetEntryCountAsync().ConfigureAwait(false);
    }
}
