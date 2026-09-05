using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ThisIsMyPC.App.Controls;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// CI-safe: the custom window frame (TitleBarControl) rendered alone. Proves
/// the icon and name draw, the caption buttons sit top-right, the maximize
/// glyph swaps to restore against the host window state, and Close still
/// goes through Window.Close so the hide-to-tray policy keeps its hook.
/// </summary>
public class TitleBarShotTests
{
    [AvaloniaFact]
    public void Frame_KeepsSearchSpacingAndCaptionButtonsInsideNarrowAndWideWindows()
    {
        var search = new TextBox { Name = "TestSearch", Width = 440, Watermark = "Search settings..." };
        var shortcuts = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        foreach (var name in new[] { "Settings", "Report a bug", "GitHub", "About" })
            shortcuts.Children.Add(new Button { Name = name.Replace(" ", string.Empty), Width = 40, Height = 40 });

        var bar = new TitleBarControl
        {
            Subtitle = "Version 1.0.0",
            CenterContent = search,
            TrailingContent = shortcuts,
        };
        using var session = UiSession.ForView(bar, new object(), "title-bar-responsive", width: 900, height: 300);

        AssertResponsiveBounds(session, bar, search, shortcuts.Children[0]);
        session.Screenshot("narrow-900");

        session.Window.Width = 933.333333;
        session.Pump();
        AssertResponsiveBounds(session, bar, search, shortcuts.Children[0]);
        session.Screenshot("reported-933");

        session.Window.Width = 1200;
        session.Pump();
        AssertResponsiveBounds(session, bar, search, shortcuts.Children[0]);
        Assert.Equal(440, search.Bounds.Width, precision: 1);
        session.Screenshot("wide-1200");
    }

    [AvaloniaFact]
    public void Frame_RendersAndCaptionButtonsWork()
    {
        var host = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var bar = new TitleBarControl();
        host.Children.Add(bar);
        using var session = UiSession.ForView(host, new object(), "title-bar", width: 900, height: 300);

        session.Screenshot("frame");
        Assert.True(session.IsTextVisible("ThisIsMyPC"));

        var maximize = session.Find<Button>(b => b.Name == "MaximizeButton");
        // Find only sees visible controls; the hidden glyph comes off the control by name.
        var maximizeGlyph = bar.FindControl<PathIcon>("MaximizeGlyph")!;
        var restoreGlyph = bar.FindControl<PathIcon>("RestoreGlyph")!;
        Assert.True(maximizeGlyph.IsVisible);
        Assert.False(restoreGlyph.IsVisible);

        session.Click(maximize);
        Assert.Equal(WindowState.Maximized, session.Window.WindowState);
        Assert.False(maximizeGlyph.IsVisible);
        Assert.True(restoreGlyph.IsVisible);
        session.Screenshot("maximized");

        session.Click(maximize);
        Assert.Equal(WindowState.Normal, session.Window.WindowState);
        Assert.True(maximizeGlyph.IsVisible);

        var close = session.Find<Button>(b => b.Classes.Contains("close"));
        session.Hover(close);
        session.Screenshot("close-hover");

        var closing = false;
        session.Window.Closing += (_, e) => { closing = true; e.Cancel = true; };
        session.Click(close);
        Assert.True(closing);
    }

    private static void AssertResponsiveBounds(UiSession session, TitleBarControl bar, Visual search, Visual settings)
    {
        var close = bar.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("close"));
        var searchBounds = BoundsInWindow(session, search);
        var settingsBounds = BoundsInWindow(session, settings);
        var closeBounds = BoundsInWindow(session, close);

        Assert.True(searchBounds.Width >= 200, $"Search width was {searchBounds.Width}.");
        Assert.True(settingsBounds.Left - searchBounds.Right >= 12,
            $"Search-to-settings gap was {settingsBounds.Left - searchBounds.Right}.");
        Assert.True(closeBounds.Right <= Math.Ceiling(session.Window.ClientSize.Width),
            $"Close ended at {closeBounds.Right}, outside client width {session.Window.ClientSize.Width}.");
    }

    private static Rect BoundsInWindow(UiSession session, Visual visual)
    {
        var origin = visual.TranslatePoint(default, session.Window)
            ?? throw new InvalidOperationException("Control is not attached to the window.");
        return new Rect(origin, visual.Bounds.Size);
    }
}
