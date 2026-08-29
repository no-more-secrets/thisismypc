using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class FirstLaunchBannerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tipc-banner-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
    }

    private sealed class StubDetector : ICapabilityDetector
    {
        public WindowsSku? Sku => WindowsSku.Pro;
        public string? SkuDetectionFailureReason => null;
        public bool IsSkuRestricted(WindowsSku? restriction) => false;
        public bool IsAvailable(SystemCapability capability) => true;
        public ModuleAvailability GetAvailability(SystemCapability capability) => new(true);
        public bool IsOwnerModeAvailable => false;
        public IReadOnlyList<CapabilityReportRow> GetCapabilityReport() =>
        [
            new(SystemCapability.HwInfo, "HWiNFO sensors", new ModuleAvailability(false, "Not detected.", "Install HWiNFO.")),
            new(SystemCapability.Registry, "Registry access", new ModuleAvailability(true)),
        ];
    }

    private (MainWindowViewModel Vm, SettingsService Settings) CreateViewModel(bool dismissed = false)
    {
        var settings = new SettingsService(Path.Combine(_dir, $"s-{Guid.NewGuid():N}.json"));
        settings.Initialize();
        if (dismissed)
            settings.SetApp("firstLaunchBannerDismissed", "1");

        var pending = new PendingChangesService();
        var reviewPanel = new ReviewPanelViewModel(pending, new Core.Sets.CustomSetWriter(Path.Combine(_dir, "sets")));
        var vm = new MainWindowViewModel(
            new NavigationService([new Fakes.FakeModule(name: "FakeModule")]),
            pending,
            new Fakes.FakeChangeHistoryService(),
            new Fakes.FakeRegistryService(),
            new Fakes.FakeExplorerRestartService(),
            reviewPanel,
            new Fakes.FakeSetProvider(),
            [],
            new Core.Sets.CustomSetWriter(Path.Combine(_dir, "sets")),
            new Fakes.FakeRestorePointService(),
            capabilityDetector: new StubDetector(),
            settingsService: settings);
        return (vm, settings);
    }

    [Fact]
    public async Task FirstLaunch_BannerShown_WithModuleAndCapabilityRows()
    {
        var (vm, _) = CreateViewModel();
        await vm.InitializeAsync();

        var home = Assert.IsType<HomeViewModel>(vm.CurrentContent);
        Assert.NotNull(home.FirstLaunchBanner);
        Assert.Contains(home.FirstLaunchBanner!.ModuleRows, r => r.Title == "FakeModule" && r.IsAvailable);
        Assert.Contains(home.FirstLaunchBanner.CapabilityRows, r => r.Title == "HWiNFO sensors" && !r.IsAvailable);
        // Only hardware-ecosystem capabilities appear — Registry says nothing useful
        Assert.DoesNotContain(home.FirstLaunchBanner.CapabilityRows, r => r.Title == "Registry access");
    }

    [Fact]
    public async Task Dismiss_PersistsAndHides_OnNextHomeBuild()
    {
        var (vm, settings) = CreateViewModel();
        await vm.InitializeAsync();
        var home = Assert.IsType<HomeViewModel>(vm.CurrentContent);

        home.FirstLaunchBanner!.DismissCommand.Execute(null);

        Assert.False(home.FirstLaunchBanner.IsVisible);
        Assert.True(settings.GetAppBool("firstLaunchBannerDismissed", false));

        vm.OpenHomeCommand.Execute(null);
        var secondHome = Assert.IsType<HomeViewModel>(vm.CurrentContent);
        Assert.Null(secondHome.FirstLaunchBanner);
    }

    [Fact]
    public async Task PreviouslyDismissed_NoBanner()
    {
        var (vm, _) = CreateViewModel(dismissed: true);
        await vm.InitializeAsync();

        var home = Assert.IsType<HomeViewModel>(vm.CurrentContent);
        Assert.Null(home.FirstLaunchBanner);
    }

    [Fact]
    public async Task NavigatingToAModule_CountsAsDismissal()
    {
        var (vm, settings) = CreateViewModel();
        await vm.InitializeAsync();
        Assert.NotNull(Assert.IsType<HomeViewModel>(vm.CurrentContent).FirstLaunchBanner);

        var item = vm.SidebarGroups.SelectMany(g => g.Items).Single();
        vm.NavigateToModuleCommand.Execute(item);

        Assert.True(settings.GetAppBool("firstLaunchBannerDismissed", false));
    }
}
