using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ThisIsMyPC.App.Views;

public partial class DisplayView : UserControl
{
    public DisplayView()
    {
        InitializeComponent();

        // Tunneled so the wheel wins over the page ScrollViewer while the
        // pointer sits on a slider; elsewhere the page scrolls as usual.
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    private static void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Source is not Visual source
            || source.FindAncestorOfType<Slider>(includeSelf: true) is not { } slider)
        {
            return;
        }

        // Snapping sliders step by their tick; continuous ones by 1/20 of
        // range (5 on a 0-100 monitor), so one notch is a visible change.
        var step = slider.IsSnapToTickEnabled && slider.TickFrequency >= 1
            ? slider.TickFrequency
            : Math.Max(1, Math.Round((slider.Maximum - slider.Minimum) / 20));

        slider.Value = Math.Clamp(
            slider.Value + Math.Sign(e.Delta.Y) * step, slider.Minimum, slider.Maximum);
        e.Handled = true;
    }

    /// <summary>
    /// The value boxes commit on focus loss; Enter releases focus so a typed
    /// value applies immediately and the box stops eating keystrokes.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter && e.Source is TextBox)
        {
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
            e.Handled = true;
        }
    }
}
