using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

/// <summary>Story 10.5: Home as the launch default with sidebar exclusivity.</summary>
public sealed class MainWindowViewModelHomeTests
{
    private static MainWindowViewModel CreateViewModel(params string[] moduleNames)
    {
        var modules = moduleNames.Length == 0
            ? new List<Core.Modules.IModule> { new Fakes.FakeModule(name: "TestModule") }
            : moduleNames.Select(Core.Modules.IModule (n) => new Fakes.FakeModule(name: n)).ToList();
        var pendingChangesService = new PendingChangesService();
        var reviewPanel = new ReviewPanelViewModel(
            pendingChangesService,
            new CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-home-{Guid.NewGuid():N}")));
        return new MainWindowViewModel(
            new NavigationService(modules),
            pendingChangesService,
            new Fakes.FakeChangeHistoryService(),
            new Fakes.FakeRegistryService(),
            new Fakes.FakeExplorerRestartService(),
            reviewPanel,
            new Fakes.FakeSetProvider(),
            [],
            new CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-home-{Guid.NewGuid():N}")),
            new Fakes.FakeRestorePointService());
    }

    [Fact]
    public async Task Launch_DefaultsToHome_NoModuleActive()
    {
        var vm = CreateViewModel();

        await vm.InitializeAsync();

        Assert.True(vm.IsHomeActive);
        Assert.IsType<HomeViewModel>(vm.CurrentContent);
        Assert.Equal("Home", vm.ContentTitle);
        Assert.Null(vm.SelectedModule);
        Assert.All(
            vm.SidebarGroups.SelectMany(g => g.Items),
            item => Assert.False(item.IsActive));
    }

    [Fact]
    public async Task NavigateToModule_ClearsHomeActive()
    {
        var vm = CreateViewModel();
        await vm.InitializeAsync();
        var item = vm.SidebarGroups.SelectMany(g => g.Items).Single();

        vm.NavigateToModuleCommand.Execute(item);

        Assert.False(vm.IsHomeActive);
        Assert.True(item.IsActive);
    }

    [Fact]
    public async Task OpenSetLoader_ThenHome_ExclusiveActiveStates()
    {
        var vm = CreateViewModel();
        await vm.InitializeAsync();

        vm.OpenSetLoaderCommand.Execute(null);
        Assert.True(vm.IsSetLoaderActive);
        Assert.False(vm.IsHomeActive);

        vm.OpenHomeCommand.Execute(null);
        Assert.True(vm.IsHomeActive);
        Assert.False(vm.IsSetLoaderActive);
        Assert.IsType<HomeViewModel>(vm.CurrentContent);
    }

}
