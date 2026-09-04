using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The title bar's right-hand shortcuts on the real MainWindow: the info
/// button opens the About card under the bar (version, publisher, links) and
/// a click elsewhere closes it; the gear lands on Settings. Real service
/// graph, so Diagnostic.
/// </summary>
[Trait("Category", "Diagnostic")]
public class HeaderShotTests
{
    [AvaloniaFact(Timeout = 120_000)]
    public async Task AboutOpensUnderTheBar_AndTheGearOpensSettings()
    {
        using var session = UiSession.ForMainWindow("header");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");

        var about = session.Find<Button>(b => b.Name == "AboutButton");
        session.Click(about);
        Assert.True(vm.IsAboutOpen);
        Assert.True(session.IsTextVisible(vm.VersionText));
        Assert.True(session.IsTextVisible(vm.PublisherText));
        session.Screenshot("about-open");

        // Clicking the info button again closes it; a click elsewhere does too.
        session.Click(about);
        Assert.False(vm.IsAboutOpen);
        session.Click(about);
        session.ClickText("Home");
        Assert.False(vm.IsAboutOpen);

        var gear = session.Find<Button>(b => AutomationProperties.GetName(b) == "Settings" && b.Classes.Contains("bar-icon"));
        session.Click(gear);
        await session.WaitForAsync(() => vm.ContentTitle == "Settings", what: "Settings page");
        session.Screenshot("settings-from-gear");
    }
}

/// <summary>
/// The sidebar has no toggle button: dragging its right edge snaps it between
/// the expanded and collapsed widths as the pointer crosses the midpoint.
/// </summary>
[Trait("Category", "Diagnostic")]
public class SidebarGripShotTests
{
    [AvaloniaFact(Timeout = 120_000)]
    public async Task DraggingTheEdge_SnapsBetweenCollapsedAndExpanded()
    {
        using var session = UiSession.ForMainWindow("sidebar-grip");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");
        Assert.False(vm.IsSidebarCollapsed);

        var grip = session.Find<Border>(b => b.Name == "SidebarGrip");
        var start = session.CenterOf(grip);
        session.Window.MouseDown(start, Avalonia.Input.MouseButton.Left);
        session.Window.MouseMove(new Avalonia.Point(60, start.Y));
        session.Pump();
        Assert.True(vm.IsSidebarCollapsed);
        session.Screenshot("collapsed-mid-drag");

        session.Window.MouseMove(new Avalonia.Point(180, start.Y));
        session.Pump();
        Assert.False(vm.IsSidebarCollapsed);
        session.Window.MouseUp(new Avalonia.Point(180, start.Y), Avalonia.Input.MouseButton.Left);
        session.Pump();
        session.Screenshot("expanded-after-drag");
    }
}
