using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.App.UiTests;

public class SettingsAppearanceShotTests
{
    [AvaloniaFact]
    public async Task HomeToggle_RefreshesBeforeStateAfterApply()
    {
        var registry = new UiFakeRegistryService();
        var queue = new PendingChangesService();
        await using var module = new ShellModule(registry);
        using var vm = new ShellViewModel(new ShellScanData(new ExplorerSettingsReader(registry).ReadAll(),
            new TaskbarSettingsReader(registry).Read()), queue, registry);
        using var session = UiSession.ForView(new ShellView(), vm, "settings-home-cycle");
        var home = vm.GeneralSettings.Single(s => s.Label == "Hide Settings Home");
        home.IsEnabled = true;
        await session.WaitForAsync(() => queue.PendingCount == 1, what: "hide Home staging");
        Assert.True((await queue.ApplyAllAsync(module.ApplyChangeAsync, module.RevertChangeAsync)).IsSuccess);
        home.IsEnabled = false;
        await session.WaitForAsync(() => queue.PendingCount == 1, what: "show Home staging");
        var show = Assert.Single(Assert.Single(queue.PendingGroups).Changes);
        Assert.Equal("hide:home", show.BeforeValue);
        Assert.Equal(ShellRegistryPaths.AbsentValue, show.AfterValue);
    }

    [AvaloniaFact]
    public async Task AppearanceChoices_ShowOnExistingExplorerTabsAndStageHome()
    {
        var registry = new UiFakeRegistryService();
        var scan = new ShellScanData(new ExplorerSettingsReader(registry).ReadAll(),
            new TaskbarSettingsReader(registry).Read());
        var queue = new PendingChangesService();
        using var vm = new ShellViewModel(scan, queue, registry);
        using var session = UiSession.ForView(new ShellView(), vm, "settings-appearance", height: 1000);
        session.ClickText("General");
        Assert.True(session.IsTextVisible("Hide Settings Home"));
        session.Screenshot("general");
        var home = vm.GeneralSettings.Single(s => s.Label == "Hide Settings Home");
        home.IsEnabled = true;
        await session.WaitForAsync(() => queue.PendingCount == 1, what: "Settings Home staging");
        Assert.Equal("hide:home", Assert.Single(Assert.Single(queue.PendingGroups).Changes).AfterValue);
        session.Screenshot("home-staged");
        session.ClickText("Desktop");
        Assert.True(session.IsTextVisible("Use dark mode in apps"));
        Assert.True(session.IsTextVisible("Use dark mode in Windows"));
        session.Screenshot("desktop");
    }
}
