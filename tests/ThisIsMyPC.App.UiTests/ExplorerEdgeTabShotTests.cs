using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.UiTests;

/// <summary>Checks Explorer's card-edge tabs and search across responsive layouts.</summary>
public class ExplorerEdgeTabShotTests
{
    [AvaloniaFact]
    public void TabsFillCardEdge_SearchStaysInContentAcrossTabsAndWidths()
    {
        var vm = new ShellViewModel(new ShellScanData([], new TaskbarSettings(1, true, false, false)),
            new PendingChangesService(), new UiFakeRegistryService());
        var card = new Border
        {
            Name = "TestCard", Margin = new Thickness(16), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Child = new ShellView(),
        };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("RaisedBrush"));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("OutlineBrush"));
        using var session = UiSession.ForView(card, vm, "explorer-edge-tabs", width: 980, height: 600);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            foreach (var width in new[] { 980, 440 })
            {
                session.Window.Width = width;
                session.Pump();
                var strip = session.Find<Border>(b => b.Name == "PART_Strip");
                Assert.Equal(card.Bounds.Width - 2, strip.Bounds.Width, 0.5);
                Assert.Equal(session.TopOf(card) + 1, session.TopOf(strip), 0.5);
                var search = session.Find<TextBox>(b => b.Watermark == "Search settings" && b.IsEffectivelyVisible);
                Assert.True(session.TopOf(search) >= session.TopOf(strip) + strip.Bounds.Height + 12);
                Assert.Equal(25, search.TranslatePoint(default, card)!.Value.X, 0.5);
                Assert.Equal(23, card.Bounds.Width - search.TranslatePoint(default, card)!.Value.X - search.Bounds.Width, 0.5);
                var tabs = session.FindAll<TabItem>(_ => true).ToArray();
                var selected = tabs.Single(t => t.IsSelected);
                var neighbor = tabs.First(t => !t.IsSelected && Math.Abs(session.TopOf(t) - session.TopOf(selected)) < 1);
                var chrome = selected.GetVisualDescendants().OfType<ThisIsMyPC.App.Controls.SelectedTabChrome>().Single();
                Assert.Equal(session.TopOf(neighbor) + neighbor.Bounds.Height, session.TopOf(chrome) + chrome.Bounds.Height - 7, 0.01);
                session.Screenshot($"{theme.Key}-{width}");
            }
        }
        var box = session.Find<TextBox>(b => b.Watermark == "Search settings" && b.IsEffectivelyVisible);
        session.Type(box, "widgets");
        Assert.Equal("widgets", vm.SearchText);
        session.ClickText("Taskbar");
        box = session.Find<TextBox>(b => b.Watermark == "Search settings" && b.IsEffectivelyVisible);
        Assert.Equal("widgets", box.Text);
        Assert.True(session.IsTextVisible("Taskbar widgets"));
        session.Screenshot("search-preserved-taskbar");
    }

    [AvaloniaFact(Timeout = 300_000)]
    [Trait("Category", "Diagnostic")]
    public async Task MainWindow_HostsExplorerAtCardEdgeAndRestoresOtherPagePadding()
    {
        using var session = UiSession.ForMainWindow("explorer-edge-main");
        session.Window.Width = 1200;
        session.Window.Height = 800;
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar");
        session.ClickText("Explorer");
        await session.WaitForAsync(() => vm.CurrentContent is ShellViewModel, timeoutMs: 120_000, what: "Explorer");
        var card = session.Find<Border>(b => b.Name == "ModuleContentHost");
        Assert.Equal(default, card.Padding);
        var strip = session.Find<Border>(b => b.Name == "PART_Strip");
        Assert.Equal(card.Bounds.Width - 2, strip.Bounds.Width, 0.5);
        Assert.Equal(session.TopOf(card) + 1, session.TopOf(strip), 0.5);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot($"{theme.Key}-1200x800-explorer");
        }
        foreach (var name in new[] { "Environment", "Context Menus", "Startup & Services", "Software", "Settings" })
        {
            session.ClickText(name);
            await session.WaitForAsync(() => vm.CurrentContent is not null && vm.ContentTitle == name,
                timeoutMs: 120_000, what: name);
            Assert.Equal(default, card.Padding);
            strip = session.Find<Border>(b => b.Name == "PART_Strip");
            Assert.Equal(card.Bounds.Width - 2, strip.Bounds.Width, 0.5);
            Assert.Equal(session.TopOf(card) + 1, session.TopOf(strip), 0.5);
            session.SetTheme(ThemeVariant.Dark);
            session.Screenshot(name.Replace(" ", "-", StringComparison.Ordinal));
        }
        // Search from a different module must survive clearing its current content.
        vm.SearchQuery = "TaskbarAl";
        session.Pump();
        session.ClickText("Taskbar alignment");
        await session.WaitForAsync(() => vm.CurrentContent is ShellViewModel { SearchText.Length: > 0 },
            timeoutMs: 120_000, what: "Explorer search destination");
        Assert.Equal(2, session.Find<TabControl>(t => t.IsEffectivelyVisible).SelectedIndex);
        Assert.True(session.IsTextVisible("Taskbar alignment (Left)"));
        session.Screenshot("global-search-taskbar");
        session.ClickText("Home");
        session.Pump();
        Assert.Equal(new Thickness(24, 12, 6, 24), card.Padding);
    }
}
