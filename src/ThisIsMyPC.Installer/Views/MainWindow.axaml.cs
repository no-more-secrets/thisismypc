using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ThisIsMyPC.Installer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Same rule as the app window: clicking empty space releases keyboard
    /// focus. Avalonia only moves focus when the click lands on a focusable
    /// control, so the folder box would otherwise stay highlighted.
    /// </summary>
    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source)
            return;

        for (Visual? v = source; v is not null; v = v.GetVisualParent())
        {
            if (v is IInputElement { Focusable: true })
                return;
        }

        FocusManager?.ClearFocus();
    }
}
