using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The Debug sidebar entry is Debug-only. In Debug builds, clicking it must
/// open the Debug page with the UI Gallery as its first tab AND show the
/// button in its active (selected) state; every tab is shot in both themes
/// and widths for the geometry tool, and the simulated-mode bar is shown and
/// reset through the shared state (in memory only; nothing is applied).
/// Release builds hide the button entirely, so the click test skips itself
/// there and the hidden-state test asserts the inverse. Boots the real
/// MainWindow, so Category=Diagnostic.
/// </summary>
[Trait("Category", "Diagnostic")]
public class GallerySidebarTests
{
    [AvaloniaFact(Timeout = 120_000)]
    public async Task DebugButton_MatchesBuildConfiguration()
    {
        using var session = UiSession.ForMainWindow("debug-sidebar");
        var viewModel = (MainWindowViewModel)session.Window.DataContext!;

        await session.WaitForAsync(
            () => viewModel.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");

        if (!MainWindowViewModel.IsDebugVisible)
        {
            // Release: the dev-facing entry must not be reachable at all.
            Assert.Empty(session.FindAll<TextBlock>(t => t.Text == "Debug"));
            Assert.DoesNotContain("UI Gallery", session.DescribeVisibleText(), StringComparison.Ordinal);
            session.Screenshot("sidebar-release-no-debug");
            return;
        }

        session.ClickText("Debug");
        await session.WaitForAsync(
            () => viewModel.ContentTitle == DebugViewModel.Title, timeoutMs: 30_000, what: "debug page load");

        Assert.True(viewModel.IsDebugActive);
        Assert.True(viewModel.UsesEdgeTabs);
        var headers = new[] { "UI Gallery", "Test Controls", "State Simulation" };
        Assert.Equal(headers, session.FindAll<TabItem>(_ => true).Select(t => t.Header as string).ToArray());
        Assert.True(session.IsTextVisible("Type scale"));

        var darkShots = new List<string>();
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        foreach (var width in new[] { 1280, 900 })
        {
            session.SetTheme(theme);
            session.Window.Width = width;
            session.Pump();
            foreach (var header in headers)
            {
                session.Click(session.Find<TabItem>(t => t.Header as string == header));
                var shot = session.Screenshot($"{header.Replace(' ', '-').ToLowerInvariant()}-{theme.Key}-{width}");
                if (theme == ThemeVariant.Dark && width == 1280)
                    darkShots.Add(shot);
            }
        }

        // Edge-geometry contract, in pixels, on the dark 1280 frames: the strip
        // is the 42px full-width header, content starts 25 in, ends 23 before
        // the card edge, and the scrollbar lane begins 10 from it.
        // Settings is the reference edge-tab page; it is shot the same way and
        // must read the same numbers, so a drift in the port shows up as a
        // mismatch on both rather than a false failure on one.
        session.SetTheme(ThemeVariant.Dark);
        session.Window.Width = 1280;
        session.Pump();
        session.ClickText("Settings");
        await session.WaitForAsync(() => viewModel.IsSettingsActive, what: "settings page");
        darkShots.Add(session.Screenshot("settings-reference-Dark-1280"));
        // Skip 46, not 42: the selected chip's fillets reach 1px past the well
        // and antialias over the next rows, and a first or last selected tab
        // puts that curve at the card's very edge (Settings reads the same).
        var report = darkShots
            .Select(shot => EdgeGeometry.Measure(shot, skipHeaderPixels: 46))
            .ToList();
        File.WriteAllLines(Path.Combine(session.ShotDirectory, "edge-geometry.txt"), report.Select(g => g.ToString()));
        foreach (var geometry in report)
        {
            Assert.True(geometry.ContentL == 25, $"{geometry.Name}: {geometry}");
            Assert.True(geometry.ContentT is >= 55 and <= 63, $"{geometry.Name}: {geometry}");
            if (geometry.LaneFrom >= 0)
                Assert.True(geometry.LaneFrom == 10, $"{geometry.Name}: {geometry}");
            // Settings fills the width; the Debug tabs cap content at 860 like the
            // Gallery always did, so their ContentR is large by design.
            if (geometry.Name.Contains("settings", StringComparison.Ordinal))
                Assert.True(geometry.ContentR == 23, $"{geometry.Name}: {geometry}");
        }
        session.ClickText("Debug");
        await session.WaitForAsync(() => viewModel.IsDebugActive, what: "debug page again");
        session.SetTheme(ThemeVariant.Dark);
        session.Window.Width = 1280;
        session.Pump();

        // Simulated mode in the real window: the bar appears above the apply bar
        // with Reset, and the page's own state follows. In memory only.
        var simulation = session.Services!.GetRequiredService<DebugSimulation>();
        simulation.Sku = WindowsSku.Home;
        session.Pump();
        Assert.True(viewModel.IsSimulationActive);
        Assert.True(session.IsTextVisible(viewModel.SimulationBannerText));
        session.Click(session.Find<TabItem>(t => t.Header as string == "State Simulation"));
        session.Screenshot("simulation-bar-Dark-1280");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("simulation-bar-Light-1280");
        session.SetTheme(ThemeVariant.Dark);

        session.ClickText("Reset");
        session.Pump();
        Assert.False(simulation.IsActive);
        Assert.False(viewModel.IsSimulationActive);
        session.Screenshot("sidebar-debug-selected");
    }
}
