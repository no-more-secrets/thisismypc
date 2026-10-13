using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.App.UiTests;

public sealed class WidgetsDefaultsShotTests
{
    [AvaloniaFact]
    public async Task WidgetsToggle_StagesMissingDefaultAndCapturesFreshBeforeState()
    {
        var registry = new UiFakeRegistryService();
        var queue = new PendingChangesService();
        await using var module = new ShellModule(registry);
        using var vm = new ShellViewModel(new ShellScanData(new ExplorerSettingsReader(registry).ReadAll(),
            new TaskbarSettingsReader(registry).Read()), queue, registry);
        using var session = UiSession.ForView(new ShellView(), vm, "widgets-defaults", height: 1000);
        session.ClickText("Taskbar");
        var widgets = vm.TaskbarSettings.Single(s => s.Label == "Taskbar widgets");
        Assert.True(widgets.IsEnabled);
        widgets.IsEnabled = false;
        await session.WaitForAsync(() => queue.PendingCount == 1, what: "hide Widgets staging");
        var hide = Assert.Single(Assert.Single(queue.PendingGroups).Changes);
        Assert.Equal(ShellRegistryPaths.AbsentValue, hide.BeforeValue);
        session.Screenshot("hide-staged");
        Assert.True((await queue.ApplyAllAsync(module.ApplyChangeAsync, module.RevertChangeAsync)).IsSuccess);
        widgets.IsEnabled = true;
        await session.WaitForAsync(() => queue.PendingCount == 1, what: "show Widgets staging");
        var show = Assert.Single(Assert.Single(queue.PendingGroups).Changes);
        Assert.Equal("0", show.BeforeValue);
        Assert.Equal("1", show.AfterValue);
    }
}
