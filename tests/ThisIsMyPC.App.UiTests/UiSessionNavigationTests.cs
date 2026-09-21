using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;

namespace ThisIsMyPC.App.UiTests;

public sealed class UiSessionNavigationTests
{
    [AvaloniaFact]
    public void ScrollAndClickText_ScrollsButtonCenterEvenWhenCaptionIsVisible()
    {
        var caption = new TextBlock { Text = "Partially clipped module" };
        var button = new Button
        {
            Content = caption,
            Height = 160,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        var panel = new StackPanel();
        panel.Children.Add(new Border { Height = 200 });
        panel.Children.Add(button);
        var scroller = new ScrollViewer { Content = panel };
        using var session = UiSession.ForView(scroller, new object(), "harness-partial-navigation", width: 320, height: 240);

        Assert.InRange(session.CenterOf(caption).Y, 0, scroller.Viewport.Height - 1);
        Assert.True(session.CenterOf(button).Y >= scroller.Viewport.Height);

        session.ScrollAndClickText("Partially clipped module");

        Assert.Equal(1, clicks);
        Assert.True(scroller.Offset.Y > 0);
        session.Screenshot("button-center-visible");
    }

    [AvaloniaFact]
    public void ScrollAndClickText_ReachesClippedButtonsInBothDirections()
    {
        var upper = new Button { Content = "Upper module" };
        var lower = new Button { Content = "Lower module" };
        var upperClicks = 0;
        var lowerClicks = 0;
        upper.Click += (_, _) => upperClicks++;
        lower.Click += (_, _) => lowerClicks++;
        var panel = new StackPanel();
        panel.Children.Add(upper);
        panel.Children.Add(new Border { Height = 1000 });
        panel.Children.Add(lower);
        var scroller = new ScrollViewer { Content = panel };
        using var session = UiSession.ForView(scroller, new object(), "harness-navigation", width: 320, height: 240);

        session.ClickText("Lower module");
        Assert.Equal(0, lowerClicks);

        session.ScrollAndClickText("Lower module");
        Assert.Equal(1, lowerClicks);
        Assert.True(scroller.Offset.Y > 0);
        session.Screenshot("lower-module");

        session.ScrollAndClickText("Upper module");
        Assert.Equal(1, upperClicks);
        Assert.Equal(0, scroller.Offset.Y);
        session.Screenshot("upper-module");
    }
}
