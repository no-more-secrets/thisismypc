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

    [AvaloniaFact]
    public async Task SelectingExplorerTabs_KeepsRenderedHeadersStableAt150Percent()
    {
        var vm = new ShellViewModel(new ShellScanData([], new TaskbarSettings(1, true, false, false)),
            new PendingChangesService(), new UiFakeRegistryService());
        using var session = UiSession.ForView(new ShellView(), vm, "explorer-tab-text-150", width: 980, height: 600);
        // Headless exposes no DPI setter. Set its backing field, then deliver
        // the same callback a native window uses when its monitor DPI changes.
        var platform = session.Window.PlatformImpl!;
        var scaling = platform.GetType().GetField("<RenderScaling>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(scaling);
        scaling.SetValue(platform, 1.5);
        var changed = (Action<double>)platform.GetType().GetProperty("ScalingChanged")!.GetValue(platform)!;
        changed(1.5);
        session.Pump();
        Assert.Equal(1.5, session.Window.RenderScaling);
        foreach (var width in new[] { 980, 440 })
        {
            session.Window.Width = width;
            session.Pump();
            var tabs = session.FindAll<TabItem>(_ => true).ToArray();
            var labels = tabs.Select(t => t.GetVisualDescendants().OfType<TextBlock>().First()).ToArray();
            var positions = labels.Select(t => t.TranslatePoint(default, session.Window)!.Value).ToArray();
            Avalonia.PixelRect[]? inkBaseline = null;
            for (var i = 0; i < tabs.Length; i++)
            {
                session.Click(tabs[i]);
                await Task.Delay(150);
                session.Pump();
                for (var j = 0; j < tabs.Length; j++)
                {
                    // The label itself survives selection. A new template must not
                    // replace it and take a different fractional-pixel layout path.
                    Assert.Same(labels[j], tabs[j].GetVisualDescendants().OfType<TextBlock>().First());
                    Assert.Equal(positions[j], labels[j].TranslatePoint(default, session.Window)!.Value);
                    Assert.True(labels[j].RenderTransform is null || labels[j].RenderTransform!.Value.IsIdentity);
                }
                using var frame = new Avalonia.Media.Imaging.RenderTargetBitmap(
                    new PixelSize((int)(width * 1.5), 900), new Vector(144, 144));
                frame.Render(session.Window);
                var path = Path.Combine(session.ShotDirectory, $"{width}-selected-{i}-150.png");
                frame.Save(path);
                using var pixels = SkiaSharp.SKBitmap.Decode(path);
                var ink = labels.Select(label => TextInkBounds(pixels, label, session.Window)).ToArray();
                inkBaseline ??= ink;
                Assert.Equal(inkBaseline, ink);
            }
        }
    }
    [AvaloniaFact]
    public void SelectedTabFill_MeetsContentWithoutADarkPixelAtFractionalDpi()
    {
        var vm = new ShellViewModel(new ShellScanData([], new TaskbarSettings(1, true, false, false)),
            new PendingChangesService(), new UiFakeRegistryService());
        var card = new Border { Child = new ShellView() };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("RaisedBrush"));
        using var session = UiSession.ForView(card, vm, "explorer-tab-seam", width: 980, height: 300);
        session.ClickText("File Explorer");
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        {
            session.SetTheme(theme);
            var platform = session.Window.PlatformImpl!;
            platform.GetType().GetField("<RenderScaling>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(platform, scale);
            ((Action<double>)platform.GetType().GetProperty("ScalingChanged")!.GetValue(platform)!)(scale);
            session.Pump();
            using var frame = new Avalonia.Media.Imaging.RenderTargetBitmap(
                new PixelSize((int)(980 * scale), (int)(300 * scale)), new Vector(96 * scale, 96 * scale));
            frame.Render(session.Window);
            var path = Path.Combine(session.ShotDirectory, $"seam-{theme.Key}-{scale:0.00}.png");
            frame.Save(path);
            using var pixels = SkiaSharp.SKBitmap.Decode(path);
            var selected = session.Find<TabItem>(t => t.IsSelected);
            var strip = session.Find<Border>(b => b.Name == "PART_Strip");
            var origin = selected.TranslatePoint(default, session.Window)!.Value;
            var x = (int)((origin.X + selected.Bounds.Width / 2) * scale);
            var seam = (int)Math.Round((session.TopOf(strip) + strip.Bounds.Height) * scale);
            var fill = pixels.GetPixel(x, seam + 4);
            for (var sampleX = (int)((origin.X + 10) * scale); sampleX < (origin.X + selected.Bounds.Width - 10) * scale; sampleX++)
            for (var y = seam - 4; y <= seam + 4; y++)
            {
                var actual = pixels.GetPixel(sampleX, y);
                Assert.True(fill == actual,
                    $"{theme.Key}, scale {scale}, pixel {sampleX},{y}: {actual}, expected {fill}; seam {seam}.");
            }
        }
    }
    private static PixelRect TextInkBounds(SkiaSharp.SKBitmap pixels, TextBlock label, Window window)
    {
        var origin = label.TranslatePoint(default, window)!.Value;
        const double scale = 1.5;
        var left = (int)Math.Floor(origin.X * scale);
        var top = (int)Math.Floor(origin.Y * scale);
        var right = (int)Math.Ceiling((origin.X + label.Bounds.Width) * scale);
        var bottom = (int)Math.Ceiling((origin.Y + label.Bounds.Height) * scale);
        var background = pixels.GetPixel(left, top - 2).Red;
        var foreground = ((Avalonia.Media.ISolidColorBrush)label.Foreground!).Color.R;
        var minX = right;
        var minY = bottom;
        var maxX = left;
        var maxY = top;
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            // Normalize selected and unselected foregrounds to glyph coverage.
            if ((pixels.GetPixel(x, y).Red - background) / (double)(foreground - background) < 0.5)
                continue;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
        Assert.True(maxX >= minX && maxY >= minY);
        return new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
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
