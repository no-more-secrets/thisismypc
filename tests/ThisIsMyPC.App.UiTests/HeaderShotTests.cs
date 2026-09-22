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

    /// <summary>
    /// Restart Explorer is a permanent header button on the Explorer page and
    /// nowhere else. Never clicked here: the real service would restart Sam's shell.
    /// </summary>
    [AvaloniaFact(Timeout = 120_000)]
    public async Task RestartExplorerButton_LivesInTheExplorerPageHeader()
    {
        using var session = UiSession.ForMainWindow("header-restart-explorer");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");

        // Find only sees visible controls: on Home there is no such button to see.
        Assert.Null(session.TryFind<Button>(b => b.Name == "RestartExplorerButton"));
        Assert.False(vm.IsExplorerPageOpen);

        session.OpenModule("Explorer");
        await session.WaitForAsync(() => vm.CurrentContent is ShellViewModel && !vm.IsModuleLoading, timeoutMs: 120_000, what: "Explorer page");
        Assert.True(vm.IsExplorerPageOpen);
        var button = session.Find<Button>(b => b.Name == "RestartExplorerButton");
        Assert.True(button.IsEffectivelyVisible);
        Assert.DoesNotContain("accent", button.Classes);
        session.Screenshot("explorer-restart-button");

        // Applied changes waiting for the restart make it the loud button.
        vm.IsRestartActionAvailable = true;
        session.Pump();
        Assert.Contains("accent", button.Classes);
        session.Screenshot("explorer-restart-button-owed");
        vm.IsRestartActionAvailable = false;

        session.OpenModule("Display");
        await session.WaitForAsync(() => vm.CurrentContent is DisplayViewModel, timeoutMs: 60_000, what: "Display page");
        Assert.False(button.IsEffectivelyVisible);
        Assert.Null(session.TryFind<Button>(b => b.Name == "RestartExplorerButton"));
    }

    /// <summary>The title row's refresh rebuilds the page: Home stays Home, Display rescans into a fresh view model.</summary>
    [AvaloniaFact(Timeout = 120_000)]
    public async Task RefreshButton_RebuildsTheCurrentPage()
    {
        using var session = UiSession.ForMainWindow("header-refresh");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");

        var refresh = session.Find<Button>(b => AutomationProperties.GetName(b) == "Refresh this page");
        var homeBefore = vm.CurrentContent;
        session.Click(refresh);
        session.Pump();
        Assert.Equal("Home", vm.ContentTitle);
        Assert.NotSame(homeBefore, vm.CurrentContent);

        session.OpenModule("Display");
        await session.WaitForAsync(() => vm.CurrentContent is DisplayViewModel && !vm.IsModuleLoading, timeoutMs: 60_000, what: "Display");
        var displayBefore = vm.CurrentContent;
        session.Click(refresh);
        await session.WaitForAsync(() => vm.CurrentContent is DisplayViewModel && !ReferenceEquals(vm.CurrentContent, displayBefore) && !vm.IsModuleLoading, timeoutMs: 60_000, what: "Display rescan");
        session.Screenshot("display-after-refresh");
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
        session.Click(grip);
        session.Pump();
        Assert.True(vm.IsSidebarCollapsed);
        session.Click(grip);
        session.Pump();
        Assert.False(vm.IsSidebarCollapsed);
        grip.Focus();
        session.Window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Space, Avalonia.Input.RawInputModifiers.None);
        session.Pump();
        Assert.True(vm.IsSidebarCollapsed);
        session.Window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Space, Avalonia.Input.RawInputModifiers.None);
        session.Pump();
        Assert.False(vm.IsSidebarCollapsed);

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
