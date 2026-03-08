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
    public async Task LoadHistory_PopulatesHistoryGroupsWithDateGrouping()
    {
        var service = new Fakes.FakeChangeHistoryService();
        var vm = CreateViewModel(service);

        await vm.LoadHistoryCommand.ExecuteAsync(null);

        // Empty history — no groups
        Assert.Empty(vm.HistoryGroups);
        Assert.Equal(0, vm.TotalEntryCount);
    }

    [Fact]
    public async Task LoadHistory_GroupsEntriesByDate()
    {
        var today = DateTimeOffset.Now;
        var yesterday = today.AddDays(-1);

        var service = new Fakes.FakeChangeHistoryServiceWithEntries(
        [
            CreateEntry(1, "today1", today),
            CreateEntry(2, "today2", today.AddMinutes(-5)),
            CreateEntry(3, "yesterday1", yesterday),
        ]);

        var vm = CreateViewModel(service);
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.HistoryGroups.Count);
        Assert.Equal("Today", vm.HistoryGroups[0].DateHeader);
        Assert.Equal(2, vm.HistoryGroups[0].Entries.Count);
        Assert.Equal("Yesterday", vm.HistoryGroups[1].DateHeader);
        Assert.Single(vm.HistoryGroups[1].Entries);
        Assert.Equal(3, vm.TotalEntryCount);
    }

    [Fact]
    public async Task RevertCommand_CallsServiceAndRefreshesList()
    {
        var revertCalled = false;

        var service = new Fakes.FakeChangeHistoryServiceWithEntries(
        [
            CreateEntry(1, "test", DateTimeOffset.Now),
        ]);

        var vm = CreateViewModel(
            service,
            revertFunc: _ =>
            {
                revertCalled = true;
                return Task.FromResult(OperationResult<bool>.Success(true));
            });

        await vm.LoadHistoryCommand.ExecuteAsync(null);
        var entry = vm.HistoryGroups[0].Entries[0];

        await vm.RevertCommand.ExecuteAsync(entry);

        Assert.True(revertCalled);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task RedoCommand_CallsServiceAndRefreshesList()
    {
        var applyCalled = false;

        var service = new Fakes.FakeChangeHistoryServiceWithEntries(
        [
            CreateEntry(1, "test", DateTimeOffset.Now, isReverted: true),
        ]);

        var vm = CreateViewModel(
            service,
            applyFunc: _ =>
            {
                applyCalled = true;
                return Task.FromResult(OperationResult<bool>.Success(true));
            });

        await vm.LoadHistoryCommand.ExecuteAsync(null);
        var entry = vm.HistoryGroups[0].Entries[0];

        await vm.RedoCommand.ExecuteAsync(entry);

        Assert.True(applyCalled);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task RevertCommand_ShowsErrorOnFailure()
    {
        var service = new Fakes.FakeChangeHistoryServiceWithEntries(
        [
            CreateEntry(1, "test", DateTimeOffset.Now),
        ],
        revertResult: OperationResult<bool>.Failure("Access denied", ErrorCategory.AccessDenied));

        var vm = CreateViewModel(service);
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        await vm.RevertCommand.ExecuteAsync(vm.HistoryGroups[0].Entries[0]);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("administrator", vm.ErrorMessage);
    }

    [Fact]
    public async Task RedoCommand_ShowsErrorOnFailure()
    {
        var service = new Fakes.FakeChangeHistoryServiceWithEntries(
        [
            CreateEntry(1, "test", DateTimeOffset.Now, isReverted: true),
        ],
        redoResult: OperationResult<bool>.Failure("Service unavailable", ErrorCategory.ServiceUnavailable));

        var vm = CreateViewModel(service);
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        await vm.RedoCommand.ExecuteAsync(vm.HistoryGroups[0].Entries[0]);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("not available", vm.ErrorMessage);
    }

    private static ChangeHistoryEntry CreateEntry(long id, string settingId, DateTimeOffset appliedAt, bool isReverted = false) => new()
    {
        Id = id,
        ModuleId = "TestModule",
        SettingId = settingId,
        DisplayName = $"Test {settingId}",
        SystemLocation = @"HKCU\Test",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Disabled",
        AfterDisplay = "Enabled",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Enable,
        AppliedAt = appliedAt,
        RevertedAt = isReverted ? appliedAt.AddMinutes(1) : null,
    };
}
