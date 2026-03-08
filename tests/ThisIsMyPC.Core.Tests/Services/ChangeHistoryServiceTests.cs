using Microsoft.Data.Sqlite;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Data;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Services;

public class ChangeHistoryServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ChangeHistoryService _service;

    public ChangeHistoryServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_history_{Guid.NewGuid():N}.db");
        var repository = new ChangeHistoryRepository();
        _service = new ChangeHistoryService(repository, _dbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private static MutationResult CreateSuccessResult(params ChangeDescriptor[] applied) => new()
    {
        IsSuccess = true,
        Applied = applied,
        RolledBack = [],
    };

    private static ChangeDescriptor CreateChange(string settingId = "setting1", string before = "0", string? after = "1") => new()
    {
        ModuleId = "TestModule",
        SettingId = settingId,
        DisplayName = $"Test {settingId}",
        SystemLocation = @"HKCU\Test",
        BeforeValue = before,
        AfterValue = after,
        BeforeDisplay = before,
        AfterDisplay = after,
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Enable,
    };

    [Fact]
    public async Task RecordChangesAsync_SavesAllAppliedChanges()
    {
        await _service.InitializeAsync();

        var result = CreateSuccessResult(CreateChange("s1"), CreateChange("s2"));
        await _service.RecordChangesAsync(result);

        var count = await _service.GetEntryCountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsEntriesInReverseChronologicalOrder()
    {
        await _service.InitializeAsync();

        await _service.RecordChangesAsync(CreateSuccessResult(CreateChange("first")));
        await Task.Delay(10); // Ensure distinct timestamps
        await _service.RecordChangesAsync(CreateSuccessResult(CreateChange("second")));

        var history = await _service.GetHistoryAsync();

        Assert.Equal(2, history.Count);
        Assert.Equal("second", history[0].SettingId);
        Assert.Equal("first", history[1].SettingId);
    }

    [Fact]
    public async Task RevertChangeAsync_SwapsValuesAndCallsRevertFunc()
    {
        await _service.InitializeAsync();

        var change = CreateChange("target", before: "OFF", after: "ON");
        await _service.RecordChangesAsync(CreateSuccessResult(change));

        var history = await _service.GetHistoryAsync();
        var entryId = history[0].Id;

        ChangeDescriptor? capturedChange = null;
        var result = await _service.RevertChangeAsync(entryId, cd =>
        {
            capturedChange = cd;
            return Task.FromResult(OperationResult<bool>.Success(true));
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedChange);
        Assert.Equal("ON", capturedChange!.BeforeValue);
        Assert.Equal("OFF", capturedChange.AfterValue);
    }

    [Fact]
    public async Task RevertChangeAsync_MarksOriginalEntryAsReverted()
    {
        await _service.InitializeAsync();

        await _service.RecordChangesAsync(CreateSuccessResult(CreateChange("target")));
        var history = await _service.GetHistoryAsync();
        var entryId = history[0].Id;

        await _service.RevertChangeAsync(entryId, _ =>
            Task.FromResult(OperationResult<bool>.Success(true)));

        var updated = (await _service.GetHistoryAsync()).First(e => e.Id == entryId);
        Assert.NotNull(updated.RevertedAt);
        Assert.NotNull(updated.RevertedByEntryId);
    }

    [Fact]
    public async Task RevertChangeAsync_InsertsNewRevertHistoryEntry()
    {
        await _service.InitializeAsync();

        await _service.RecordChangesAsync(CreateSuccessResult(CreateChange("target")));
        var history = await _service.GetHistoryAsync();

        await _service.RevertChangeAsync(history[0].Id, _ =>
            Task.FromResult(OperationResult<bool>.Success(true)));

        var count = await _service.GetEntryCountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task RevertChangeAsync_ReturnsFailureWhenRevertFuncFails()
    {
        await _service.InitializeAsync();

        await _service.RecordChangesAsync(CreateSuccessResult(CreateChange("target")));
        var history = await _service.GetHistoryAsync();

        var result = await _service.RevertChangeAsync(history[0].Id, _ =>
            Task.FromResult(OperationResult<bool>.Failure("Access denied", ErrorCategory.AccessDenied)));

        Assert.False(result.IsSuccess);
        Assert.Equal("Access denied", result.ErrorMessage);
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
    }

    [Fact]
    public async Task RedoChangeAsync_ReAppliesOriginalValuesAndCallsApplyFunc()
    {
        await _service.InitializeAsync();

        var change = CreateChange("target", before: "OFF", after: "ON");
        await _service.RecordChangesAsync(CreateSuccessResult(change));
        var history = await _service.GetHistoryAsync();

        // First revert
        await _service.RevertChangeAsync(history[0].Id, _ =>
            Task.FromResult(OperationResult<bool>.Success(true)));

        // Then redo
        ChangeDescriptor? capturedChange = null;
        var result = await _service.RedoChangeAsync(history[0].Id, cd =>
        {
            capturedChange = cd;
            return Task.FromResult(OperationResult<bool>.Success(true));
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedChange);
        Assert.Equal("OFF", capturedChange!.BeforeValue);
        Assert.Equal("ON", capturedChange.AfterValue);
    }

    [Fact]
    public async Task RedoChangeAsync_ClearsRevertedAtOnOriginalEntry()
    {
        await _service.InitializeAsync();

        await _service.RecordChangesAsync(CreateSuccessResult(CreateChange("target")));
        var history = await _service.GetHistoryAsync();
        var entryId = history[0].Id;

        await _service.RevertChangeAsync(entryId, _ =>
            Task.FromResult(OperationResult<bool>.Success(true)));

        await _service.RedoChangeAsync(entryId, _ =>
            Task.FromResult(OperationResult<bool>.Success(true)));

        var updated = (await _service.GetHistoryAsync()).First(e => e.Id == entryId);
        Assert.Null(updated.RevertedAt);
        Assert.Null(updated.RevertedByEntryId);
    }

    [Fact]
    public async Task RedoChangeAsync_ReturnsFailureWhenApplyFuncFails()
    {
        await _service.InitializeAsync();

        await _service.RecordChangesAsync(CreateSuccessResult(CreateChange("target")));
        var history = await _service.GetHistoryAsync();

        await _service.RevertChangeAsync(history[0].Id, _ =>
            Task.FromResult(OperationResult<bool>.Success(true)));

        var result = await _service.RedoChangeAsync(history[0].Id, _ =>
            Task.FromResult(OperationResult<bool>.Failure("Service unavailable", ErrorCategory.ServiceUnavailable)));

        Assert.False(result.IsSuccess);
        Assert.Equal("Service unavailable", result.ErrorMessage);
    }

    [Fact]
    public async Task RevertChangeAsync_ReturnsFailureForNonexistentEntry()
    {
        await _service.InitializeAsync();

        var result = await _service.RevertChangeAsync(999, _ =>
            Task.FromResult(OperationResult<bool>.Success(true)));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task RedoChangeAsync_ReturnsFailureForNonRevertedEntry()
    {
        await _service.InitializeAsync();

        await _service.RecordChangesAsync(CreateSuccessResult(CreateChange("target")));
        var history = await _service.GetHistoryAsync();

        var result = await _service.RedoChangeAsync(history[0].Id, _ =>
            Task.FromResult(OperationResult<bool>.Success(true)));

        Assert.False(result.IsSuccess);
    }
}
