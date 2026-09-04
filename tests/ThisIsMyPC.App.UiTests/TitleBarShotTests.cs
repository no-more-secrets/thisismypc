using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
}
