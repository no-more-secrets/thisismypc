using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.App.UiTests;

public sealed class WidgetsPolicyShotTests
{
    [AvaloniaFact]
    public async Task AllowWidgets_StagesPolicyAndCanEnableAfterApply()
    {
        var registry = new UiFakeRegistryService();
        var queue = new PendingChangesService();
        await using var module = new ShellModule(registry);
        using var vm = new ShellViewModel(new ShellScanData(new ExplorerSettingsReader(registry).ReadAll(),
            new TaskbarSettingsReader(registry).Read()), queue, registry, windowsSku: ThisIsMyPC.Core.Modules.WindowsSku.Pro);
        using var session = UiSession.ForView(new ShellView(), vm, "widgets-policy", height: 1000);
        session.ClickText("Taskbar");
        var widgets = vm.TaskbarSettings.Single(s => s.Label == "Allow Widgets");
        Assert.True(widgets.IsEnabled);
        widgets.IsEnabled = false;
        await session.WaitForAsync(() => queue.PendingCount == 1, what: "disable Widgets policy");
        Assert.Equal(ShellRegistryPaths.AbsentValue, Assert.Single(Assert.Single(queue.PendingGroups).Changes).BeforeValue);
        session.Screenshot("disable-staged");
        Assert.True((await queue.ApplyAllAsync(module.ApplyChangeAsync, module.RevertChangeAsync)).IsSuccess);
        Assert.True(widgets.IsToggleEnabled);
        widgets.IsEnabled = true;
        await session.WaitForAsync(() => queue.PendingCount == 1, what: "enable Widgets policy");
        var show = Assert.Single(Assert.Single(queue.PendingGroups).Changes);
        Assert.Equal("0", show.BeforeValue);
        Assert.Equal("1", show.AfterValue);
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData(ThisIsMyPC.Core.Modules.WindowsSku.Home)]
    public void AllowWidgets_UnsupportedEditionCannotStage(ThisIsMyPC.Core.Modules.WindowsSku? sku)
    {
        var registry = new UiFakeRegistryService();
        var queue = new PendingChangesService();
        using var vm = new ShellViewModel(new ShellScanData(new ExplorerSettingsReader(registry).ReadAll(),
            new TaskbarSettingsReader(registry).Read()), queue, registry, windowsSku: sku);
        var widgets = vm.TaskbarSettings.Single(s => s.Label == "Allow Widgets");
        Assert.False(widgets.IsToggleEnabled);
        widgets.IsEnabled = false;
        Assert.Equal(0, queue.PendingCount);
    }
}
