using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The shared tab strip drawn as browser tabs: unselected chips float 6px
/// above the well's floor, the selected chip reaches through the floor and
/// carries the page fill so it reads as part of the content below, and the
/// strip stays 42px tall for one row. Hosted on the Environment page with
/// fake variables. CI-safe: nothing reads the system.
/// </summary>
public class TabStripShotTests
{
    private const double StripHeight = 42;
    private const double ChipHeight = 28;
    private const double Float = 6;
    private const double Overhang = 1;

    private static EnvironmentScanData ScanData() => new(
        UserVariables:
        [
            new EnvironmentVariable("Path", @"C:\Users\me\.dotnet\tools;C:\Users\me\AppData\Roaming\npm", EnvironmentVariableScope.User),
            new EnvironmentVariable("GOPATH", @"%USERPROFILE%\go", EnvironmentVariableScope.User),
        ],
        SystemVariables:
        [
            new EnvironmentVariable("Path", @"C:\Windows\system32;C:\Windows", EnvironmentVariableScope.System),
            new EnvironmentVariable("windir", @"C:\Windows", EnvironmentVariableScope.System),
        ]);

    private static UiSession Open(string suite)
        => UiSession.ForView(new EnvironmentView(), new EnvironmentViewModel(ScanData(), new PendingChangesService()), suite);

    private static UiSession OpenNarrow(out TabControl tabControl)
    {
        tabControl = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "General", Content = new TextBlock { Text = "General content" } },
                new TabItem { Header = "Appearance", Content = new TextBlock { Text = "Appearance content" } },
                new TabItem { Header = "Behavior", Content = new TextBlock { Text = "Behavior content" } },
                new TabItem { Header = "Advanced", Content = new TextBlock { Text = "Advanced content" } },
                new TabItem { Header = "Privacy", Content = new TextBlock { Text = "Privacy content" } },
                new TabItem { Header = "Updates", Content = new TextBlock { Text = "Updates content" } },
                new TabItem { Header = "Network", Content = new TextBlock { Text = "Network content" } },
                new TabItem { Header = "About", Content = new TextBlock { Text = "About content" } },
            },
            SelectedIndex = 1,
        };
        return UiSession.ForView(tabControl, new object(), "tab-strip-multirow", width: 300, height: 240);
    }

    private static Border Strip(UiSession session) => session.Find<Border>(b => b.Name == "PART_Strip");

    private static TabItem Tab(UiSession session, string headerPrefix)
        => session.Find<TabItem>(t => t.Header is string h && h.StartsWith(headerPrefix, StringComparison.Ordinal));

    private static double BottomOf(UiSession session, Control control) => session.TopOf(control) + control.Bounds.Height;

    private static void AssertConnected(UiSession session, TabItem selected, TabItem floating)
    {
        var strip = Strip(session);
        var floor = BottomOf(session, strip);

        Assert.True(selected.IsSelected);
        Assert.False(floating.IsSelected);
        Assert.Equal(StripHeight, strip.Bounds.Height, 0.5);
        // The selected chip stands on the floor: its bottom is the well's bottom.
        Assert.Equal(ChipHeight + Float + Overhang, selected.Bounds.Height, 0.5);
        Assert.Equal(floor, BottomOf(session, selected), 0.5);
        // A floating chip stops short of the floor by its float and the well's border.
        Assert.Equal(ChipHeight, floating.Bounds.Height, 0.5);
        Assert.Equal(floor - Float - 1, BottomOf(session, floating), 0.5);
        // Both labels sit on the same line.
        Assert.Equal(session.TopOf(selected), session.TopOf(floating), 0.5);
    }

    [AvaloniaFact]
    public void SelectedTab_ReachesTheFloorAndTheOthersFloat()
    {
        using var session = Open("tab-strip");

        session.Screenshot("path-selected");
        AssertConnected(session, Tab(session, "PATH"), Tab(session, "System"));

        session.Hover(Tab(session, "System"));
        session.Screenshot("system-hovered");
        Assert.True(Tab(session, "PATH").IsSelected);

        session.Click(Tab(session, "System"));
        session.Screenshot("system-selected");
        AssertConnected(session, Tab(session, "System"), Tab(session, "PATH"));
    }

    [AvaloniaFact]
    public void Strip_RendersInLightTheme()
    {
        using var session = Open("tab-strip");
        try
        {
            session.SetTheme(ThemeVariant.Light);
            session.Screenshot("path-selected-light");
            AssertConnected(session, Tab(session, "PATH"), Tab(session, "System"));
            session.Click(Tab(session, "User"));
            session.Screenshot("user-selected-light");
            AssertConnected(session, Tab(session, "User"), Tab(session, "PATH"));
        }
        finally
        {
            session.SetTheme(ThemeVariant.Dark);
        }
    }

    [AvaloniaFact]
    public void MultirowStrip_PlacesSelectedRowAgainstContent_AfterSelectionChanges()
    {
        using var session = OpenNarrow(out var tabControl);
        var tabs = session.FindAll<TabItem>(_ => true).ToList();
        var strip = Strip(session);

        session.Screenshot("appearance-selected");
        Assert.Equal(3, tabs.Select(tab => Math.Round(session.TopOf(tab))).Distinct().Count());
        Assert.Equal(tabs.Max(tab => session.TopOf(tab)), session.TopOf(tabs[1]), 0.5);
        Assert.Equal(StripHeight + 2 * (ChipHeight + Float), strip.Bounds.Height, 0.5);
        Assert.All(tabs, tab => Assert.True(BottomOf(session, tab) <= BottomOf(session, strip) + 0.5));

        session.Click(tabs[6]);
        tabs = session.FindAll<TabItem>(_ => true).ToList();
        session.Screenshot("network-selected");

        Assert.Equal(6, tabControl.SelectedIndex);
        Assert.Equal(tabs.Max(tab => session.TopOf(tab)), session.TopOf(tabs[6]), 0.5);
        Assert.Equal(BottomOf(session, strip), BottomOf(session, tabs[6]), 0.5);
        Assert.All(tabs.Where(tab => !tab.IsSelected), tab => Assert.Equal(ChipHeight, tab.Bounds.Height, 0.5));
    }
}
