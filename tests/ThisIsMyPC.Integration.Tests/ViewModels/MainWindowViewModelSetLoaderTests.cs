using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class MainWindowViewModelSetLoaderTests
{
    private const string TestModuleId = "FakeModule";

    private static (MainWindowViewModel Vm, Fakes.FakeSetProvider SetProvider) CreateViewModel(
        params string[] extraModuleNames)
    {
        var fakeModule = new Fakes.FakeModule(name: TestModuleId);
        var modules = new List<Core.Modules.IModule> { fakeModule };
        modules.AddRange(extraModuleNames.Select(n => new Fakes.FakeModule(name: n)));
        var navigationService = new NavigationService(modules);
        var pendingChangesService = new PendingChangesService();
        var historyService = new Fakes.FakeChangeHistoryService();
        var reviewPanel = new ReviewPanelViewModel(pendingChangesService);
        var registryService = new Fakes.FakeRegistryService();
        var explorerRestartService = new Fakes.FakeExplorerRestartService();
        var setProvider = new Fakes.FakeSetProvider();
        var vm = new MainWindowViewModel(
            navigationService, pendingChangesService, historyService, registryService,
            explorerRestartService, reviewPanel, setProvider, []);
        return (vm, setProvider);
    }

    [Fact]
    public async Task OpenSetLoader_ShowsSetLoaderContent_AndDeactivatesModules()
    {
        var (vm, setProvider) = CreateViewModel();
        await vm.InitializeAsync();

        vm.OpenSetLoaderCommand.Execute(null);

        Assert.IsType<SetLoaderViewModel>(vm.CurrentContent);
        Assert.True(vm.IsSetLoaderActive);
        Assert.Equal("Set Loader", vm.ContentTitle);
        Assert.Null(vm.SelectedModule);
        Assert.All(
            vm.SidebarGroups.SelectMany(g => g.Items),
            item => Assert.False(item.IsActive));
        Assert.Equal(1, setProvider.LoadCount);
    }

    [Fact]
    public async Task OpenSetLoader_EachOpen_ReloadsSetsFromDisk()
    {
        var (vm, setProvider) = CreateViewModel();
        await vm.InitializeAsync();

        vm.OpenSetLoaderCommand.Execute(null);
        vm.OpenSetLoaderCommand.Execute(null);

        Assert.Equal(2, setProvider.LoadCount);
    }

    [Fact]
    public async Task NavigateBackToCurrentModule_LeavesSetLoader_AndReactivatesItem()
    {
        var (vm, _) = CreateViewModel();
        await vm.InitializeAsync();
        var moduleItem = vm.SidebarGroups.SelectMany(g => g.Items).Single();

        vm.OpenSetLoaderCommand.Execute(null);
        vm.NavigateToModuleCommand.Execute(moduleItem);

        // Synchronous effects only: the content rebuild itself runs on the Avalonia
        // dispatcher, which has no message loop in unit tests.
        Assert.False(vm.IsSetLoaderActive);
        Assert.True(moduleItem.IsActive);
        Assert.Same(moduleItem, vm.SelectedModule);
    }

    [Fact]
    public async Task NavigateToDifferentModule_FromSetLoader_ActivatesThatModule()
    {
        var (vm, _) = CreateViewModel("SecondModule");
        await vm.InitializeAsync();
        var second = vm.SidebarGroups.SelectMany(g => g.Items).Single(i => i.Name == "SecondModule");

        vm.OpenSetLoaderCommand.Execute(null);
        vm.NavigateToModuleCommand.Execute(second);

        Assert.False(vm.IsSetLoaderActive);
        Assert.True(second.IsActive);
        Assert.Same(second, vm.SelectedModule);
        Assert.False(
            vm.SidebarGroups.SelectMany(g => g.Items).Single(i => i.Name == TestModuleId).IsActive);
    }
}
