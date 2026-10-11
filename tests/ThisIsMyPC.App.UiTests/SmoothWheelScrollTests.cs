using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ThisIsMyPC.App.Controls;

namespace ThisIsMyPC.App.UiTests;

public sealed class SmoothWheelScrollTests
{
    [Fact]
    public void EaseOut_SlowsWithoutOvershoot_AndSettles()
    {
        var motion = new WheelScrollMotion();
        var maximum = new Vector(1000, 1000);
        motion.Push(default, new Vector(0, 50), maximum, 0);
        var first = motion.Advance(0.025, maximum).Y;
        var second = motion.Advance(0.05, maximum).Y;
        Assert.InRange(first, 1, 49);
        Assert.InRange(second - first, 0.01, first);
        Assert.Equal(50, motion.Advance(1, maximum).Y);
        Assert.False(motion.IsActive);
    }

    [Fact]
    public void EaseOut_IsIndependentOfFrameRate()
    {
        var maximum = new Vector(1000, 1000);
        var slow = new WheelScrollMotion();
        var fast = new WheelScrollMotion();
        slow.Push(default, new Vector(0, 100), maximum, 0);
        fast.Push(default, new Vector(0, 100), maximum, 0);
        for (var i = 1; i < 12; i++) fast.Advance(i / 120d, maximum);
        Assert.Equal(slow.Advance(0.1, maximum).Y, fast.Advance(0.1, maximum).Y, 8);
    }

    [Fact]
    public void RepeatedInput_HasCappedRamp_AndReversalDropsPendingTravel()
    {
        var maximum = new Vector(10000, 10000);
        var motion = new WheelScrollMotion();
        var position = new Vector(0, 500);
        for (var i = 0; i < 10; i++)
        {
            motion.Push(position, new Vector(0, 50), maximum, i * 0.05);
            position = motion.Advance(i * 0.05 + 0.02, maximum);
        }
        var end = motion.Advance(2, maximum);
        Assert.Equal(1096, end.Y, 6); // 50 + 54 + 58 + seven steps capped at 62.
        motion.Push(end, new Vector(0, 50), maximum, 3);
        position = motion.Advance(3.01, maximum);
        motion.Push(position, new Vector(0, -50), maximum, 3.02);
        Assert.True(motion.Advance(3.03, maximum).Y < position.Y);
        Assert.Equal(position.Y - 50, motion.Advance(4, maximum).Y, 6);
    }

    [Fact]
    public void PauseAndStop_ResetRamp_AndBoundsClampTravel()
    {
        var maximum = new Vector(100, 100);
        var motion = new WheelScrollMotion();
        motion.Push(new Vector(0, 90), new Vector(0, 50), maximum, 0);
        Assert.Equal(100, motion.Advance(1, maximum).Y);
        motion.Push(new Vector(0, 50), new Vector(0, -20), maximum, 2);
        var position = motion.Advance(2.02, maximum);
        motion.Push(position, new Vector(0, -20), maximum, 2.2);
        Assert.Equal(position.Y - 20, motion.Advance(3, maximum).Y, 6);
        motion.Stop();
        motion.Push(new Vector(0, 50), new Vector(0, 20), maximum, 3.01);
        Assert.Equal(70, motion.Advance(4, maximum).Y);
        motion.Push(new Vector(0, 90), new Vector(0, 50), maximum, 5);
        Assert.Equal(30, motion.Advance(6, new Vector(100, 30)).Y);
    }

    [AvaloniaFact]
    public async Task Wheel_EasesOnRealPresenter_AndExternalScrollCancels()
    {
        var viewer = Page();
        using var session = UiSession.ForView(viewer, new object(), "smooth-wheel", width: 600, height: 400);
        var presenter = viewer.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        Assert.True(SmoothWheelScroll.GetIsEnabled(presenter));
        session.Screenshot("before");
        session.Window.MouseWheel(new Point(200, 150), new Vector(0, -1));
        Assert.InRange(viewer.Offset.Y, 0, 49.99);
        await session.WaitForAsync(() => viewer.Offset.Y > 1, what: "wheel animation");
        session.Screenshot("moving");
        await session.WaitForAsync(() => Math.Abs(viewer.Offset.Y - 50) < 0.01);
        session.Screenshot("settled");
        session.Window.MouseWheel(new Point(200, 150), new Vector(0, -1));
        viewer.Offset = new Vector(0, 200);
        await Task.Delay(350);
        session.Pump();
        Assert.Equal(200, viewer.Offset.Y);
    }

    [AvaloniaFact]
    public async Task NestedViewer_ConsumesWheel_ThenChainsAtEdge()
    {
        var inner = Page();
        inner.Height = 180;
        var outer = new ScrollViewer { Content = new StackPanel { Children = { inner, new Border { Height = 900 } } } };
        using var session = UiSession.ForView(outer, new object(), "smooth-wheel-nested", width: 600, height: 400);
        session.Window.MouseWheel(new Point(200, 80), new Vector(0, -1));
        await session.WaitForAsync(() => Math.Abs(inner.Offset.Y - 50) < 0.01);
        Assert.Equal(0, outer.Offset.Y);
        inner.Offset = new Vector(0, inner.Extent.Height - inner.Viewport.Height);
        session.Pump();
        session.Window.MouseWheel(new Point(200, 80), new Vector(0, -1));
        await session.WaitForAsync(() => Math.Abs(outer.Offset.Y - 50) < 0.01);
    }

    [AvaloniaFact]
    public async Task ShiftWheel_ScrollsHorizontally_AndDisablingRestoresImmediateInput()
    {
        var viewer = Page();
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        ((StackPanel)viewer.Content!).Width = 1200;
        using var session = UiSession.ForView(viewer, new object(), "smooth-wheel-horizontal", width: 600, height: 400);
        session.Window.MouseWheel(new Point(200, 150), new Vector(0, -1), RawInputModifiers.Shift);
        await session.WaitForAsync(() => Math.Abs(viewer.Offset.X - 50) < 0.01);
        Assert.Equal(0, viewer.Offset.Y);
        var presenter = viewer.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        SmoothWheelScroll.SetIsEnabled(presenter, false);
        session.Window.MouseWheel(new Point(200, 150), new Vector(0, -1));
        Assert.Equal(50, viewer.Offset.Y);
    }

    [AvaloniaFact]
    public async Task ReversalBeforeFirstFrame_CancelsTravelAtBoundary()
    {
        var viewer = Page();
        using var session = UiSession.ForView(viewer, new object(), "smooth-wheel-reversal", width: 600, height: 400);
        var presenter = viewer.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        RaiseWheel(presenter, session.Window, -1);
        Assert.Equal(0, viewer.Offset.Y);
        RaiseWheel(presenter, session.Window, 1);
        await Task.Delay(350);
        session.Pump();
        Assert.Equal(0, viewer.Offset.Y);
    }

    [AvaloniaFact]
    public async Task DetachAndReattach_DropsOldFrame_AndAcceptsNewInput()
    {
        var viewer = Page();
        using var session = UiSession.ForView(viewer, new object(), "smooth-wheel-detach", width: 600, height: 400);
        var presenter = viewer.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        RaiseWheel(presenter, session.Window, -1);
        session.Window.Content = null;
        session.Window.Content = viewer;
        session.Pump();
        Assert.Equal(0, viewer.Offset.Y);
        RaiseWheel(presenter, session.Window, -1);
        await session.WaitForAsync(() => Math.Abs(viewer.Offset.Y - 50) < 0.01);
    }

    private static void RaiseWheel(ScrollContentPresenter presenter, Window window, double delta) =>
        presenter.RaiseEvent(new PointerWheelEventArgs(presenter, new Pointer(1, PointerType.Mouse, true),
            window, new Point(200, 150), 0, new PointerPointProperties(), KeyModifiers.None, new Vector(0, delta)));

    private static ScrollViewer Page()
    {
        var rows = new StackPanel { Spacing = 8, Margin = new Thickness(24, 16, 22, 24) };
        for (var i = 1; i <= 30; i++)
            rows.Children.Add(new Border
            {
                Height = 60, CornerRadius = new CornerRadius(6), Background = Brushes.DimGray,
                Child = new TextBlock { Text = $"Scroll row {i}", Margin = new Thickness(16) }
            });
        return new ScrollViewer { Content = rows };
    }
}
