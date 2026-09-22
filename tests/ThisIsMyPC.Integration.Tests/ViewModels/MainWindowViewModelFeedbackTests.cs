using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

/// <summary>
/// A card's outcome never sits inside the card: the window routes a reported
/// result to a toast and a failure to the status line. Also the sidebar
/// accordion: one group open, the one holding the open module.
/// </summary>
public sealed class MainWindowViewModelFeedbackTests
{
    private static (MainWindowViewModel Vm, UserFeedbackHub Hub) CreateViewModel(params IModule[] modules)
    {
        var hub = new UserFeedbackHub();
        var pendingChangesService = new PendingChangesService();
        var reviewPanel = new ReviewPanelViewModel(
            pendingChangesService,
            new CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-feedback-{Guid.NewGuid():N}")),
            feedback: hub);
        var vm = new MainWindowViewModel(
            new NavigationService(modules.Length == 0 ? [new Fakes.FakeModule(name: "TestModule")] : modules),
            pendingChangesService,
            new Fakes.FakeChangeHistoryService(),
            new Fakes.FakeRegistryService(),
            new Fakes.FakeExplorerRestartService(),
            reviewPanel,
            new Fakes.FakeSetProvider(),
            [],
            new CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-feedback-{Guid.NewGuid():N}")),
            new Fakes.FakeRestorePointService(),
            userFeedback: hub);
        return (vm, hub);
    }

    [Fact]
    public void Fail_ShowsOnTheStatusLine_InTheErrorColour()
    {
        var (vm, hub) = CreateViewModel();

        hub.Fail("The device rejected the lighting change.");

        Assert.Equal("The device rejected the lighting change.", vm.StatusMessage);
        Assert.True(vm.IsStatusError);
        Assert.Empty(vm.ToastStack.Toasts);
    }

    [Fact]
    public void Report_ShowsAToast_AndLeavesTheStatusLineAlone()
    {
        var (vm, hub) = CreateViewModel();

        hub.Report("ASUS ROG STRIX B550-F", "Applied and saved to device");

        var toast = Assert.Single(vm.ToastStack.Toasts);
        Assert.Equal("ASUS ROG STRIX B550-F", toast.Title);
        Assert.Equal("Applied and saved to device", toast.Message);
        Assert.True(toast.IsSuccess);
        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    [Fact]
    public void Hub_RefusesBlankMessages()
    {
        var hub = new UserFeedbackHub();
        Assert.Throws<ArgumentException>(() => hub.Fail(" "));
        Assert.Throws<ArgumentException>(() => hub.Report("Title", ""));
        Assert.Throws<ArgumentException>(() => hub.Report("", "Message"));
    }

    [Fact]
    public async Task Sidebar_OpensTheGroupOfTheOpenModule_AndFoldsTheRest()
    {
        var (vm, _) = CreateViewModel(
            new Fakes.FakeModule(name: "CoreModule", group: ModuleGroup.Core),
            new Fakes.FakeModule(name: "HardwareModule", group: ModuleGroup.Hardware));
        await vm.InitializeAsync();
        var core = vm.SidebarGroups.Single(g => g.GroupName == "CORE");
        var hardware = vm.SidebarGroups.Single(g => g.GroupName == "HARDWARE");

        // Home: the first group is open so the sidebar never shows only headers.
        Assert.True(core.IsExpanded);
        Assert.False(hardware.IsExpanded);
        Assert.Equal("CORE", core.HeaderDescription);
        Assert.Equal("Open HARDWARE", hardware.HeaderDescription);

        vm.NavigateToModuleCommand.Execute(hardware.Items.Single());
        Assert.False(core.IsExpanded);
        Assert.True(hardware.IsExpanded);
        Assert.True(hardware.Items.Single().IsActive);

        // Home, Presets, and Settings keep the last group open.
        vm.OpenHomeCommand.Execute(null);
        Assert.False(core.IsExpanded);
        Assert.True(hardware.IsExpanded);
        vm.OpenSettingsCommand.Execute(null);
        Assert.True(hardware.IsExpanded);
    }

    [Fact]
    public async Task Sidebar_FoldedHeader_OpensTheGroupsFirstModule_AndAnOpenHeaderDoesNothing()
    {
        var (vm, _) = CreateViewModel(
            new Fakes.FakeModule(name: "CoreModule", group: ModuleGroup.Core),
            new Fakes.FakeModule(name: "HardwareModule", group: ModuleGroup.Hardware, available: false),
            new Fakes.FakeModule(name: "SystemModule", group: ModuleGroup.System));
        await vm.InitializeAsync();
        var core = vm.SidebarGroups.Single(g => g.GroupName == "CORE");
        var hardware = vm.SidebarGroups.Single(g => g.GroupName == "HARDWARE");
        var system = vm.SidebarGroups.Single(g => g.GroupName == "SYSTEM");

        vm.OpenSidebarGroupCommand.Execute(system);
        Assert.True(system.IsExpanded);
        Assert.False(core.IsExpanded);
        Assert.Same(system.Items.Single(), vm.SelectedModule);
        Assert.True(system.Items.Single().IsActive);

        // Clicking the header of the open group changes nothing.
        vm.OpenSidebarGroupCommand.Execute(system);
        Assert.True(system.IsExpanded);
        Assert.Same(system.Items.Single(), vm.SelectedModule);

        // A group whose modules are all unavailable cannot open through its header.
        Assert.False(hardware.Items.Single().IsAvailable);
        vm.OpenSidebarGroupCommand.Execute(hardware);
        Assert.False(hardware.IsExpanded);
        Assert.True(system.IsExpanded);
        Assert.Same(system.Items.Single(), vm.SelectedModule);
    }
}
