using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ThisIsMyPC.App.Views;

public partial class DisplayView : UserControl
{
    public DisplayView()
    {
        InitializeComponent();

        // The wheel only scrolls the page. It never moves a slider: this is
        // a page people scroll, the Advanced list especially, and a wheel
        // notch landing on a slider would write to the monitor (Sam,
        // 2026-09-04). Avalonia's Slider has no wheel handling of its own.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
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
