using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private static MainWindowViewModel CreateViewModel(out PendingChangesService pendingChangesService, params IModule[] modules)
    {
        return CreateViewModel(out pendingChangesService, out _, modules);
    }

    private static MainWindowViewModel CreateViewModel(
        out PendingChangesService pendingChangesService,
        out Fakes.FakeExplorerRestartService explorerRestartService,
        params IModule[] modules)
    {
        var navigationService = new NavigationService(modules);
        pendingChangesService = new PendingChangesService();
        var historyService = new Fakes.FakeChangeHistoryService();
        var reviewPanel = new ReviewPanelViewModel(pendingChangesService, new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-mw-{Guid.NewGuid():N}")));
        var registryService = new Fakes.FakeRegistryService();
        explorerRestartService = new Fakes.FakeExplorerRestartService();
        return new MainWindowViewModel(navigationService, pendingChangesService, historyService, registryService, explorerRestartService, reviewPanel, new Fakes.FakeSetProvider(), [], new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-mw-{Guid.NewGuid():N}")));
    }

    private static ChangeDescriptor CreateTestChange(string moduleId = "test", string settingId = "setting1") => new()
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

    [Fact]
    public void PendingCount_StartsAtZero()
    {
        var vm = CreateViewModel(out _);
        Assert.Equal(0, vm.PendingCount);
    }

    [Fact]
    public void HasPendingChanges_IsFalseWhenNoPendingChanges()
    {
        var vm = CreateViewModel(out _);
        Assert.False(vm.HasPendingChanges);
    }

    [Fact]
    public void IsApplying_StartsAsFalse()
    {
        var vm = CreateViewModel(out _);
        Assert.False(vm.IsApplying);
    }

    [Fact]
    public void PendingCount_UpdatesWhenChangeStaged()
    {
        var vm = CreateViewModel(out var service);

        service.Stage(CreateTestChange());

        Assert.Equal(1, vm.PendingCount);
        Assert.True(vm.HasPendingChanges);
    }

    [Fact]
    public void PendingCount_ResetsWhenDiscardAll()
    {
        var vm = CreateViewModel(out var service);
        service.Stage(CreateTestChange());

        service.DiscardAll();

        Assert.Equal(0, vm.PendingCount);
        Assert.False(vm.HasPendingChanges);
    }

    [Fact]
    public void PendingCountText_ShowsNoPendingChangesWhenZero()
    {
        var vm = CreateViewModel(out _);
        Assert.Equal("No pending changes", vm.PendingCountText);
    }

    [Fact]
    public void PendingCountText_ShowsCountWhenPending()
    {
        var vm = CreateViewModel(out var service);

        service.Stage(CreateTestChange("mod", "s1"));
        Assert.Equal("1 change pending", vm.PendingCountText);

        service.Stage(CreateTestChange("mod", "s2"));
        Assert.Equal("2 changes pending", vm.PendingCountText);
    }

    [Fact]
    public void DiscardAllCommand_ClearsPendingAndClosesReviewPanel()
    {
        var vm = CreateViewModel(out var service);
        service.Stage(CreateTestChange());
        vm.IsReviewPanelOpen = true;

        vm.DiscardAllCommand.Execute(null);

        Assert.Equal(0, vm.PendingCount);
        Assert.False(vm.IsReviewPanelOpen);
    }

    [Fact]
    public void OpenReviewPanelCommand_SetsIsReviewPanelOpen()
    {
        var vm = CreateViewModel(out _);

        vm.OpenReviewPanelCommand.Execute(null);

        Assert.True(vm.IsReviewPanelOpen);
    }

    [Fact]
    public async Task ApplyAllCommand_RoutesToCorrectModuleAndClears()
    {
        var applied = false;
        var fakeModule = new Fakes.FakeModule("TestModule", _ =>
        {
            applied = true;
            return Task.FromResult(OperationResult<bool>.Success(true));
        });

        var vm = CreateViewModel(out var service, fakeModule);
        await vm.InitializeAsync();

        service.Stage(CreateTestChange("TestModule", "s1"));
        vm.IsReviewPanelOpen = true;

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.True(applied);
        Assert.Equal(0, vm.PendingCount);
        Assert.False(vm.IsReviewPanelOpen);
        Assert.Equal("Changes applied successfully", vm.StatusMessage);
    }

    [Fact]
    public async Task ApplyAllCommand_ShowsErrorOnFailure()
    {
        var fakeModule = new Fakes.FakeModule("TestModule", _ =>
            Task.FromResult(OperationResult<bool>.Failure(
                "Registry access denied",
                ErrorCategory.AccessDenied)));

        var vm = CreateViewModel(out var service, fakeModule);
        await vm.InitializeAsync();

        service.Stage(CreateTestChange("TestModule", "s1"));

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Contains("Registry access denied", vm.StatusMessage);
        Assert.Contains("Access denied", vm.StatusMessage);
    }

    [Fact]
    public async Task ApplyAllCommand_DoesNothingWhenNoPendingChanges()
    {
        var vm = CreateViewModel(out _);

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    [Fact]
    public async Task ApplyAllCommand_ShowsRestartNotification_WhenExplorerRestartRequired()
    {
        var fakeModule = new Fakes.FakeModule("Explorer", _ =>
            Task.FromResult(OperationResult<bool>.Success(true)));

        var vm = CreateViewModel(out var service, fakeModule);
        await vm.InitializeAsync();

        service.Stage(new ChangeDescriptor
        {
            ModuleId = "Explorer",
            SettingId = "classic-menu",
            DisplayName = "Classic context menu",
            SystemLocation = @"HKCU\Test",
            BeforeValue = "0",
            AfterValue = "1",
            BeforeDisplay = "Disabled",
            AfterDisplay = "Enabled",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Enable,
            RestartRequirement = RestartRequirement.ExplorerRestart,
        });

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.True(vm.IsRestartNotificationVisible);
        Assert.Contains("Explorer restart required", vm.RestartNotificationMessage);
    }

    [Fact]
    public async Task ApplyAllCommand_RebootNotice_NoExplorerAction_WhenOnlyRebootRequired()
    {
        var fakeModule = new Fakes.FakeModule("TestModule", _ =>
            Task.FromResult(OperationResult<bool>.Success(true)));

        var vm = CreateViewModel(out var service, fakeModule);
        await vm.InitializeAsync();

        service.Stage(CreateTestChange("TestModule", "hags") with
        {
            RestartRequirement = RestartRequirement.Reboot,
        });

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.True(vm.IsRestartNotificationVisible);
        Assert.Contains("reboot", vm.RestartNotificationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsRestartActionAvailable);
    }

    [Fact]
    public async Task ApplyAllCommand_RebootPlusExplorer_KeepsExplorerRestartAction()
    {
        var fakeModule = new Fakes.FakeModule("TestModule", _ =>
            Task.FromResult(OperationResult<bool>.Success(true)));

        var vm = CreateViewModel(out var service, fakeModule);
        await vm.InitializeAsync();

        service.Stage(CreateTestChange("TestModule", "hags") with
        {
            RestartRequirement = RestartRequirement.Reboot,
        });
        service.Stage(CreateTestChange("TestModule", "game-dvr") with
        {
            RestartRequirement = RestartRequirement.ExplorerRestart,
        });

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.True(vm.IsRestartNotificationVisible);
        Assert.Contains("reboot", vm.RestartNotificationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Explorer", vm.RestartNotificationMessage, StringComparison.Ordinal);
        Assert.True(vm.IsRestartActionAvailable);
    }

    [Fact]
    public async Task ApplyAllCommand_SignOutNotice_WhenSignOutRequired()
    {
        var fakeModule = new Fakes.FakeModule("TestModule", _ =>
            Task.FromResult(OperationResult<bool>.Success(true)));

        var vm = CreateViewModel(out var service, fakeModule);
        await vm.InitializeAsync();

        service.Stage(CreateTestChange("TestModule", "sticky-keys") with
        {
            RestartRequirement = RestartRequirement.SignOut,
        });

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.True(vm.IsRestartNotificationVisible);
        Assert.Contains("Sign out", vm.RestartNotificationMessage, StringComparison.Ordinal);
        Assert.False(vm.IsRestartActionAvailable);
    }

    [Fact]
    public async Task ApplyAllCommand_NoRestartNotification_WhenNoRestartRequired()
    {
        var fakeModule = new Fakes.FakeModule("TestModule", _ =>
            Task.FromResult(OperationResult<bool>.Success(true)));

        var vm = CreateViewModel(out var service, fakeModule);
        await vm.InitializeAsync();

        service.Stage(CreateTestChange("TestModule", "s1"));

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.False(vm.IsRestartNotificationVisible);
        Assert.Equal("Changes applied successfully", vm.StatusMessage);
    }

    [Fact]
    public void DismissRestartNotificationCommand_HidesNotification()
    {
        var vm = CreateViewModel(out _);
        vm.IsRestartNotificationVisible = true;

        vm.DismissRestartNotificationCommand.Execute(null);

        Assert.False(vm.IsRestartNotificationVisible);
    }

    [Fact]
    public async Task RestartExplorerCommand_CallsServiceAndClearsNotification()
    {
        var vm = CreateViewModel(out _, out var restartService);
        vm.IsRestartNotificationVisible = true;
        vm.RestartNotificationMessage = "Explorer restart required";

        await vm.RestartExplorerCommand.ExecuteAsync(null);

        Assert.True(restartService.WasCalled);
        Assert.False(vm.IsRestartNotificationVisible);
        Assert.False(vm.IsRestartingExplorer);
        Assert.Equal("Explorer restarted successfully", vm.StatusMessage);
    }

    [Fact]
    public async Task RestartExplorerCommand_ShowsErrorOnFailure()
    {
        var vm = CreateViewModel(out _, out var restartService);
        restartService.ShouldSucceed = false;
        vm.IsRestartNotificationVisible = true;

        await vm.RestartExplorerCommand.ExecuteAsync(null);

        Assert.True(restartService.WasCalled);
        Assert.True(vm.IsRestartNotificationVisible);
        Assert.False(vm.IsRestartingExplorer);
        Assert.Contains("Failed to restart Explorer", vm.StatusMessage);
    }
}
