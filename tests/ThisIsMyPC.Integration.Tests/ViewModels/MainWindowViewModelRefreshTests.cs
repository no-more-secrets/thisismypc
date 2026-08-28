using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class MainWindowViewModelRefreshTests
{
    private const string TestModuleId = "TestModule";

    private static async Task<(MainWindowViewModel Vm, PendingChangesService Pending, Fakes.FakeExplorerRestartService Explorer)> CreateViewModelAsync()
    {
        var fakeModule = new Fakes.FakeModule(name: TestModuleId);
        var navigationService = new NavigationService([fakeModule]);
        await navigationService.InitializeAsync();
        var pendingChangesService = new PendingChangesService();
        var historyService = new Fakes.FakeChangeHistoryService();
        var reviewPanel = new ReviewPanelViewModel(pendingChangesService);
        var registryService = new Fakes.FakeRegistryService();
        var explorerRestartService = new Fakes.FakeExplorerRestartService();
        var vm = new MainWindowViewModel(navigationService, pendingChangesService, historyService, registryService, explorerRestartService, reviewPanel, new Fakes.FakeSetProvider(), []);
        return (vm, pendingChangesService, explorerRestartService);
    }

    private static ChangeDescriptor CreateChangeWithRestart(RestartRequirement restart) => new()
    {
        ModuleId = TestModuleId,
        SettingId = $"setting-{restart}",
        DisplayName = $"Test {restart}",
        SystemLocation = @"HKCU\Test",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Off",
        AfterDisplay = "On",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Modify,
        RestartRequirement = restart,
    };

    [Fact]
    public async Task ExplorerRefresh_shows_refresh_notification()
    {
        var (vm, pending, explorer) = await CreateViewModelAsync();
        var change = CreateChangeWithRestart(RestartRequirement.ExplorerRefresh);
        pending.Stage(new ChangeGroup
        {
            GroupId = "g1",
            DisplayName = "Test",
            Description = "Test",
            Changes = [change],
        });

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.True(vm.IsRestartNotificationVisible);
        Assert.Contains("F5", vm.RestartNotificationMessage);
        Assert.False(vm.IsRestartActionAvailable);
        Assert.True(explorer.RefreshWasCalled);
    }

    [Fact]
    public async Task ExplorerRestart_shows_restart_notification_with_action()
    {
        var (vm, pending, explorer) = await CreateViewModelAsync();
        var change = CreateChangeWithRestart(RestartRequirement.ExplorerRestart);
        pending.Stage(new ChangeGroup
        {
            GroupId = "g1",
            DisplayName = "Test",
            Description = "Test",
            Changes = [change],
        });

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.True(vm.IsRestartNotificationVisible);
        Assert.Contains("restart", vm.RestartNotificationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(vm.IsRestartActionAvailable);
        Assert.False(explorer.RefreshWasCalled);
    }

    [Fact]
    public async Task Both_restart_and_refresh_shows_restart_notification_only()
    {
        var (vm, pending, explorer) = await CreateViewModelAsync();
        pending.Stage(new ChangeGroup
        {
            GroupId = "g1",
            DisplayName = "Test",
            Description = "Test",
            Changes =
            [
                CreateChangeWithRestart(RestartRequirement.ExplorerRestart),
                CreateChangeWithRestart(RestartRequirement.ExplorerRefresh),
            ],
        });

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.True(vm.IsRestartNotificationVisible);
        Assert.True(vm.IsRestartActionAvailable);
        Assert.Contains("restart", vm.RestartNotificationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("F5", vm.RestartNotificationMessage);
        Assert.False(explorer.RefreshWasCalled);
    }

    [Fact]
    public async Task No_restart_requirement_shows_no_notification()
    {
        var (vm, pending, _) = await CreateViewModelAsync();
        var change = CreateChangeWithRestart(RestartRequirement.None);
        pending.Stage(new ChangeGroup
        {
            GroupId = "g1",
            DisplayName = "Test",
            Description = "Test",
            Changes = [change],
        });

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.False(vm.IsRestartNotificationVisible);
    }

    [Fact]
    public void RefreshExplorerViewsAsync_exists_on_interface()
    {
        var fake = new Fakes.FakeExplorerRestartService();
        IExplorerRestartService service = fake;

        var task = service.RefreshExplorerViewsAsync();
        Assert.NotNull(task);
    }
}
