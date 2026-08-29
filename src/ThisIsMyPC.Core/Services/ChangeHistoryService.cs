using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Data;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

public sealed class ChangeHistoryService : IChangeHistoryService
{
    private readonly ChangeHistoryRepository _repository;
    private readonly string _dbPath;
    private readonly IEnforcementExecutor? _enforcementExecutor;
    private readonly Drift.IDriftBaselineStore? _driftBaseline;

    public ChangeHistoryService(
        ChangeHistoryRepository repository,
        string? dbPath = null,
        IEnforcementExecutor? enforcementExecutor = null,
        Drift.IDriftBaselineStore? driftBaseline = null)
    {
        _repository = repository;
        _dbPath = dbPath ?? Path.Combine(AppConstants.DataDirectoryPath, "history.db");
        _enforcementExecutor = enforcementExecutor;
        _driftBaseline = driftBaseline;
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

        var entries = result.Applied.Select(change => new ChangeHistoryEntry
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
            Enforcement = change.Enforcement,
        }).ToList();

        await _repository.InsertBatchAsync(entries).ConfigureAwait(false);

        // 28-3: successful mutations become the watchdog's expected state.
        _driftBaseline?.RecordApplied(result.Applied);
    }

    /// <summary>
    /// 28-3: records system-initiated reversions (drift) as SystemReversion rows —
    /// distinct from user changes, never undo/redo targets from this path.
    /// </summary>
    public async Task RecordDriftEventsAsync(IReadOnlyList<ChangeHistoryEntry> driftEntries)
    {
        if (driftEntries.Count == 0)
            return;
        await _repository.InsertBatchAsync(driftEntries).ConfigureAwait(false);
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
            BeforeValue = entry.AfterValue ?? string.Empty,
            AfterValue = entry.BeforeValue,
            BeforeDisplay = entry.AfterDisplay ?? string.Empty,
            AfterDisplay = entry.BeforeDisplay,
            ValueType = entry.ValueType,
            Category = entry.Category,
            Enforcement = entry.Enforcement,
        };

        // Same routing rule as PendingChangesService: Enforcement != null goes through
        // the executor (revert direction — GPCache cleared after the primary revert so
        // e.g. the WU orchestrator can't keep enforcing the undone policy).
        var result = await RouteAsync(revertDescriptor, revertFunc, revert: true).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return OperationResult<bool>.Failure(
                result.ErrorMessage ?? "Revert operation failed",
                result.ErrorCategory ?? ErrorCategory.ServiceUnavailable);
        }

        var now = DateTimeOffset.UtcNow;

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
            GroupId = Guid.NewGuid().ToString("N"),
            AppliedAt = now,
            Enforcement = entry.Enforcement,
        };

        var insertedRevert = await _repository.InsertAsync(revertEntry).ConfigureAwait(false);
        await _repository.UpdateRevertedAtAsync(historyId, now, insertedRevert.Id)
            .ConfigureAwait(false);

        // Undo changes the expected state too — a stale expectation would report the
        // user's own undo as drift at next boot.
        _driftBaseline?.RecordApplied([revertDescriptor]);

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

        if (entry.BeforeValue is null || entry.BeforeDisplay is null)
        {
            return OperationResult<bool>.Failure(
                "Cannot redo: original before-state is missing",
                ErrorCategory.NotFound);
        }

        var redoDescriptor = new ChangeDescriptor
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
            Enforcement = entry.Enforcement,
        };

        var result = await RouteAsync(redoDescriptor, applyFunc, revert: false).ConfigureAwait(false);

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
            GroupId = Guid.NewGuid().ToString("N"),
            AppliedAt = DateTimeOffset.UtcNow,
            RedoOfEntryId = historyId,
            Enforcement = entry.Enforcement,
        };

        await _repository.InsertAsync(redoEntry).ConfigureAwait(false);
        await _repository.ClearRevertedAtAsync(historyId).ConfigureAwait(false);

        _driftBaseline?.RecordApplied([redoDescriptor]);

        return OperationResult<bool>.Success(true);
    }

    /// <summary>
    /// Enforcement routing for history undo/redo, mirroring PendingChangesService:
    /// Enforcement != null → executor (never a silent bare mutation), null → delegate.
    /// An enforced entry with no executor configured fails loudly instead of degrading.
    /// </summary>
    private async Task<OperationResult<bool>> RouteAsync(
        ChangeDescriptor descriptor,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> primaryFunc,
        bool revert)
    {
        if (descriptor.Enforcement is null)
            return await primaryFunc(descriptor).ConfigureAwait(false);

        if (_enforcementExecutor is null)
        {
            return OperationResult<bool>.Failure(
                $"'{descriptor.DisplayName}' requires enforcement but no IEnforcementExecutor is configured.",
                ErrorCategory.ServiceUnavailable);
        }

        var enforcement = revert
            ? await _enforcementExecutor.RevertAsync(descriptor, primaryFunc).ConfigureAwait(false)
            : await _enforcementExecutor.ExecuteAsync(descriptor, primaryFunc).ConfigureAwait(false);

        return enforcement.IsSuccess
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure(
                enforcement.ErrorMessage ?? "Enforcement execution failed",
                enforcement.ErrorCategory ?? ErrorCategory.ServiceUnavailable);
    }

    public async Task<IReadOnlyList<ChangeHistoryEntry>> GetRecentGroupedAsync(int groupLimit = 50)
    {
        return await _repository.GetRecentGroupedAsync(groupLimit).ConfigureAwait(false);
    }

    public async Task<int> GetGroupCountAsync()
    {
        return await _repository.GetGroupCountAsync().ConfigureAwait(false);
    }

    public async Task<int> GetEntryCountAsync()
    {
        return await _repository.GetEntryCountAsync().ConfigureAwait(false);
    }

    public async Task ClearHistoryAsync()
    {
        await _repository.DeleteAllAsync().ConfigureAwait(false);
    }
}
