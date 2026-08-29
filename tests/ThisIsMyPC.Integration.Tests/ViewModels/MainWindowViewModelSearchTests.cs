using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Search;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class MainWindowViewModelSearchTests
{
    private sealed class StubContributor : ISearchSettingsContributor
    {
        public string ModuleId => "FakeModule";
        public IReadOnlyList<SearchEntry> GetSearchEntries() =>
            [new("FakeModule", "s1", "Taskbar alignment", "Align it.", ["TaskbarAl"])];
    }

    private static MainWindowViewModel CreateViewModel()
    {
        var pending = new PendingChangesService();
        var reviewPanel = new ReviewPanelViewModel(pending, new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-search-{Guid.NewGuid():N}")));
        return new MainWindowViewModel(
            new NavigationService([new Fakes.FakeModule(name: "FakeModule")]),
            pending,
            new Fakes.FakeChangeHistoryService(),
            new Fakes.FakeRegistryService(),
            new Fakes.FakeExplorerRestartService(),
            reviewPanel,
            new Fakes.FakeSetProvider(),
            [],
            new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-search-{Guid.NewGuid():N}")),
            new Fakes.FakeRestorePointService(),
            searchContributors: [new StubContributor()]);
    }

    [Fact]
    public async Task Typing_PopulatesResults_ClearingEmptiesThem()
    {
        var vm = CreateViewModel();
        await vm.InitializeAsync();

        vm.SearchQuery = "taskbar";
        Assert.True(vm.HasSearchResults);
        var result = Assert.Single(vm.SearchResults);
        Assert.Equal("Taskbar alignment", result.Name);
        Assert.Equal("FakeModule", result.ModuleLine);

        vm.SearchQuery = "";
        Assert.False(vm.HasSearchResults);
        Assert.Empty(vm.SearchResults);
    }

    [Fact]
    public async Task SelectingAResult_NavigatesToTheModule_AndClearsTheQuery()
    {
        var vm = CreateViewModel();
        await vm.InitializeAsync();
        vm.SearchQuery = "TaskbarAl";
        var result = Assert.Single(vm.SearchResults);

        vm.SelectSearchResultCommand.Execute(result);

        // FakeModule has no content-VM branch, so assert navigation state, not title
        Assert.Equal("FakeModule", vm.SelectedModule?.Name);
        Assert.False(vm.IsHomeActive);
        Assert.Equal(string.Empty, vm.SearchQuery);
        Assert.Contains("Taskbar alignment", vm.StatusMessage, StringComparison.Ordinal);
    }
}
