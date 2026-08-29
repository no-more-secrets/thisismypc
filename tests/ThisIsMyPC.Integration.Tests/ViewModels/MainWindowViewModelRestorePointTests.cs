using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public class MainWindowViewModelRestorePointTests
{
    private static MainWindowViewModel CreateViewModel(
        out PendingChangesService pendingChangesService,
        out Fakes.FakeRestorePointService restorePointService,
        params IModule[] modules)
    {
        var navigationService = new NavigationService(modules);
        pendingChangesService = new PendingChangesService();
        var reviewPanel = new ReviewPanelViewModel(pendingChangesService, new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-rp-{Guid.NewGuid():N}")));
        restorePointService = new Fakes.FakeRestorePointService();
        return new MainWindowViewModel(
            navigationService,
            pendingChangesService,
            new Fakes.FakeChangeHistoryService(),
            new Fakes.FakeRegistryService(),
            new Fakes.FakeExplorerRestartService(),
            reviewPanel,
            new Fakes.FakeSetProvider(),
            [],
            new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-rp-{Guid.NewGuid():N}")),
            restorePointService);
    }

    private static ChangeDescriptor CreateTestChange(string settingId, string moduleId = "FakeModule") => new()
    {
        ModuleId = moduleId,
        SettingId = settingId,
        DisplayName = "Test Setting",
        SystemLocation = @"HKLM\Test",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Disabled",
        AfterDisplay = "Enabled",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Enable,
    };

    private static void StageChanges(PendingChangesService service, int count)
    {
        for (var i = 0; i < count; i++)
            service.Stage(CreateTestChange($"setting-{i}"));
    }

    private static ChangeGroup CreateGroup(string groupId, int changeCount) => new()
    {
        GroupId = groupId,
        DisplayName = $"Group {groupId}",
        Description = $"Group {groupId}",
        Changes = [.. Enumerable.Range(0, changeCount).Select(i => CreateTestChange($"{groupId}-s{i}"))],
    };

    [Fact]
    public async Task ManualCommand_Success_SetsSuccessStatus_AndPassesTimestampedDescription()
    {
        var vm = CreateViewModel(out _, out var restorePoints);

        await vm.CreateRestorePointCommand.ExecuteAsync(null);

        Assert.Equal(1, restorePoints.CallCount);
        Assert.StartsWith("ThisIsMyPC restore point ", restorePoints.Descriptions[0], StringComparison.Ordinal);
        Assert.Equal("Restore point created successfully", vm.StatusMessage);
        Assert.False(vm.IsCreatingRestorePoint);
    }

    [Fact]
    public async Task ManualCommand_Failure_ShowsRemediationMessage()
    {
        var vm = CreateViewModel(out _, out var restorePoints);
        restorePoints.NextResult = new RestorePointResult
        {
            Outcome = RestorePointOutcome.SystemRestoreDisabled,
            Message = "System Restore is disabled — enable it in System Properties > System Protection",
        };

        await vm.CreateRestorePointCommand.ExecuteAsync(null);

        Assert.Contains("System Restore is disabled", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAll_FourChanges_DoesNotCreateRestorePoint()
    {
        var module = new Fakes.FakeModule(name: "FakeModule");
        var vm = CreateViewModel(out var pending, out var restorePoints, module);
        await vm.InitializeAsync();
        StageChanges(pending, 4);

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Equal(0, restorePoints.CallCount);
        Assert.Equal(0, vm.PendingCount);
    }

    [Fact]
    public async Task ApplyAll_FiveChangesAcrossGroups_CreatesRestorePointFirst()
    {
        var module = new Fakes.FakeModule(name: "FakeModule");
        var vm = CreateViewModel(out var pending, out var restorePoints, module);
        await vm.InitializeAsync();
        StageChanges(pending, 5);

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Equal(1, restorePoints.CallCount);
        Assert.Equal("ThisIsMyPC: Before applying 5 changes", restorePoints.Descriptions[0]);
        Assert.Equal(0, vm.PendingCount);
    }

    [Fact]
    public async Task ApplyAll_SingleGroupWithSixChanges_CreatesRestorePoint()
    {
        // FR64 counts descriptors, not groups: one staged set with 6 entries must trigger.
        var module = new Fakes.FakeModule(name: "FakeModule");
        var vm = CreateViewModel(out var pending, out var restorePoints, module);
        await vm.InitializeAsync();
        pending.Stage(CreateGroup("set-batch", 6));

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Equal(1, restorePoints.CallCount);
        Assert.Equal("ThisIsMyPC: Before applying 6 changes", restorePoints.Descriptions[0]);
    }

    [Fact]
    public async Task ApplyAll_RestorePointFails_AbortsApply_ThenSecondApplyProceedsWithout()
    {
        var module = new Fakes.FakeModule(name: "FakeModule");
        var vm = CreateViewModel(out var pending, out var restorePoints, module);
        await vm.InitializeAsync();
        StageChanges(pending, 5);
        restorePoints.NextResult = new RestorePointResult
        {
            Outcome = RestorePointOutcome.Failed,
            Message = "Restore point creation failed (Windows error 5)",
        };

        await vm.ApplyAllCommand.ExecuteAsync(null);

        // Aborted: nothing applied, changes still pending, user told how to proceed
        Assert.Equal(5, pending.PendingGroups.Sum(g => g.Changes.Count));
        Assert.Contains("Click Apply again to proceed without a restore point", vm.StatusMessage, StringComparison.Ordinal);

        await vm.ApplyAllCommand.ExecuteAsync(null);

        // Second click proceeds without another creation attempt
        Assert.Equal(1, restorePoints.CallCount);
        Assert.Equal(0, vm.PendingCount);
    }

    [Fact]
    public async Task ApplyAll_DiscardAllResetsFailureAcknowledgement()
    {
        var module = new Fakes.FakeModule(name: "FakeModule");
        var vm = CreateViewModel(out var pending, out var restorePoints, module);
        await vm.InitializeAsync();
        StageChanges(pending, 5);
        restorePoints.NextResult = new RestorePointResult { Outcome = RestorePointOutcome.Failed };

        await vm.ApplyAllCommand.ExecuteAsync(null);
        Assert.Equal(1, restorePoints.CallCount);

        vm.DiscardAllCommand.Execute(null);
        StageChanges(pending, 5);
        restorePoints.NextResult = new RestorePointResult { Outcome = RestorePointOutcome.Created, SequenceNumber = 2 };

        await vm.ApplyAllCommand.ExecuteAsync(null);

        // Acknowledgement was reset by Discard All, so a fresh attempt happens
        Assert.Equal(2, restorePoints.CallCount);
        Assert.Equal(0, vm.PendingCount);
    }
}
