using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public class ChangeHistoryViewModelTests
{
    private static ChangeHistoryViewModel CreateViewModel(
        IChangeHistoryService historyService,
        Func<ChangeDescriptor, Task<OperationResult<bool>>>? revertFunc = null,
        Func<ChangeDescriptor, Task<OperationResult<bool>>>? applyFunc = null)
    {
        return new ChangeHistoryViewModel(
            historyService,
            revertFunc ?? (_ => Task.FromResult(OperationResult<bool>.Success(true))),
            applyFunc ?? (_ => Task.FromResult(OperationResult<bool>.Success(true))));
    }

    [Fact]
    public async Task LoadHistory_EmptyState()
    {
        var service = new Fakes.FakeChangeHistoryService();
        var vm = CreateViewModel(service);

        await vm.LoadHistoryCommand.ExecuteAsync(null);

        Assert.Empty(vm.HistoryGroups);
        Assert.Equal(0, vm.TotalGroupCount);
    }

    [Fact]
    public async Task LoadHistory_GroupsByDateThenBatch()
    {
        // Anchor at local noon so "-5 minutes" can never cross midnight into yesterday
        // (this test failed for real when run at 00:03).
        var today = new DateTimeOffset(DateTime.Today.AddHours(12));
        var yesterday = today.AddDays(-1);

        var service = new Fakes.FakeChangeHistoryServiceWithEntries(
        [
            CreateEntry(1, "today1", today, groupId: "g1"),
            CreateEntry(2, "today2", today.AddMinutes(-5), groupId: "g2"),
            CreateEntry(3, "yesterday1", yesterday, groupId: "g3"),
        ]);

        var vm = CreateViewModel(service);
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.HistoryGroups.Count);
        Assert.Equal("Today", vm.HistoryGroups[0].DateHeader);
        Assert.Equal(2, vm.HistoryGroups[0].Batches.Count);
        Assert.Equal("Yesterday", vm.HistoryGroups[1].DateHeader);
        Assert.Single(vm.HistoryGroups[1].Batches);
    }

    [Fact]
    public async Task LoadHistory_MultiEntryBatchShowsAsOneBatch()
    {
        var now = DateTimeOffset.Now;

        var service = new Fakes.FakeChangeHistoryServiceWithEntries(
        [
            CreateEntry(1, "ctx-7zip", now, groupId: "g1", displayName: "Context menu: 7-Zip"),
            CreateEntry(2, "ctx-7zip", now, groupId: "g1", displayName: "Context menu: 7-Zip"),
            CreateEntry(3, "ctx-7zip", now, groupId: "g1", displayName: "Context menu: 7-Zip"),
        ]);

        var vm = CreateViewModel(service);
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        Assert.Single(vm.HistoryGroups);
        Assert.Single(vm.HistoryGroups[0].Batches);

        var batch = vm.HistoryGroups[0].Batches[0];
        Assert.Equal("Context menu: 7-Zip", batch.DisplayName);
        Assert.Equal(3, batch.DetailCount);
        Assert.True(batch.HasMultipleDetails);
    }

    [Fact]
    public async Task LoadHistory_MixedBatchShowsCommaNames()
    {
        var now = DateTimeOffset.Now;

        var service = new Fakes.FakeChangeHistoryServiceWithEntries(
        [
            CreateEntry(1, "s1", now, groupId: "g1", displayName: "Context menu: 7-Zip"),
            CreateEntry(2, "s2", now, groupId: "g1", displayName: "Show Hidden Files"),
        ]);

        var vm = CreateViewModel(service);
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        var batch = vm.HistoryGroups[0].Batches[0];
        Assert.Contains("7-Zip", batch.DisplayName);
        Assert.Contains("Show Hidden Files", batch.DisplayName);
    }

    [Fact]
    public async Task RestoreCommand_CallsServiceAndRefreshesList()
    {
        var revertCalled = false;

        var service = new Fakes.FakeChangeHistoryServiceWithEntries(
        [
            CreateEntry(1, "test", DateTimeOffset.Now, groupId: "g1"),
        ]);

        var vm = CreateViewModel(
            service,
            revertFunc: _ =>
            {
                revertCalled = true;
                return Task.FromResult(OperationResult<bool>.Success(true));
            });

        await vm.LoadHistoryCommand.ExecuteAsync(null);
        var entry = vm.HistoryGroups[0].Batches[0].Details[0];

        await vm.RestoreCommand.ExecuteAsync(entry);

        Assert.True(revertCalled);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task RedoCommand_CallsServiceAndRefreshesList()
    {
        var applyCalled = false;

        var service = new Fakes.FakeChangeHistoryServiceWithEntries(
        [
            CreateEntry(1, "test", DateTimeOffset.Now, isReverted: true, groupId: "g1"),
        ]);

        var vm = CreateViewModel(
            service,
            applyFunc: _ =>
            {
                applyCalled = true;
                return Task.FromResult(OperationResult<bool>.Success(true));
            });

        await vm.LoadHistoryCommand.ExecuteAsync(null);
        var entry = vm.HistoryGroups[0].Batches[0].Details[0];

        await vm.RedoCommand.ExecuteAsync(entry);

        Assert.True(applyCalled);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task RestoreCommand_ShowsErrorOnFailure()
    {
        var service = new Fakes.FakeChangeHistoryServiceWithEntries(
        [
            CreateEntry(1, "test", DateTimeOffset.Now, groupId: "g1"),
        ],
        revertResult: OperationResult<bool>.Failure("Access denied", ErrorCategory.AccessDenied));

        var vm = CreateViewModel(service);
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        await vm.RestoreCommand.ExecuteAsync(vm.HistoryGroups[0].Batches[0].Details[0]);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("Access denied", vm.ErrorMessage);
    }

    [Fact]
    public async Task RedoCommand_ShowsErrorOnFailure()
    {
        var service = new Fakes.FakeChangeHistoryServiceWithEntries(
        [
            CreateEntry(1, "test", DateTimeOffset.Now, isReverted: true, groupId: "g1"),
        ],
        redoResult: OperationResult<bool>.Failure("Service unavailable", ErrorCategory.ServiceUnavailable));

        var vm = CreateViewModel(service);
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        await vm.RedoCommand.ExecuteAsync(vm.HistoryGroups[0].Batches[0].Details[0]);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("not available", vm.ErrorMessage);
    }

    private static ChangeHistoryEntry CreateEntry(
        long id,
        string settingId,
        DateTimeOffset appliedAt,
        bool isReverted = false,
        string? groupId = null,
        string? displayName = null) => new()
    {
        Id = id,
        ModuleId = "TestModule",
        SettingId = settingId,
        DisplayName = displayName ?? $"Test {settingId}",
        SystemLocation = @"HKCU\Test",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Disabled",
        AfterDisplay = "Enabled",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Enable,
        GroupId = groupId,
        AppliedAt = appliedAt,
        RevertedAt = isReverted ? appliedAt.AddMinutes(1) : null,
    };
}
