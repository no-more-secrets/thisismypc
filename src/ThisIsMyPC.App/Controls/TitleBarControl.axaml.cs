using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ThisIsMyPC.App.Controls;

/// <summary>
/// The app's own window frame: drag region, double-click maximize, and the
/// three caption buttons. Hosted at the top of a Window that extends its
/// client area over the system title bar with the stock chrome hidden.
/// </summary>
public partial class TitleBarControl : UserControl
{
    /// <summary>Sits left of the app icon; the host puts the sidebar toggle here.</summary>
    public static readonly StyledProperty<object?> LeadingContentProperty =
        AvaloniaProperty.Register<TitleBarControl, object?>(nameof(LeadingContent));

    /// <summary>Centred on the whole bar; the host puts the settings search here.</summary>
    public static readonly StyledProperty<object?> CenterContentProperty =
        AvaloniaProperty.Register<TitleBarControl, object?>(nameof(CenterContent));

    public object? LeadingContent
    {
        get => GetValue(LeadingContentProperty);
        set => SetValue(LeadingContentProperty, value);
    }

    public object? CenterContent
    {
        get => GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    private Window? _window;

    public TitleBarControl()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is null)
            return;

        _window.PropertyChanged += OnWindowPropertyChanged;
        UpdateMaximizeGlyph(_window.WindowState);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
            _window.PropertyChanged -= OnWindowPropertyChanged;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && e.NewValue is WindowState state)
            UpdateMaximizeGlyph(state);
    }

    private void UpdateMaximizeGlyph(WindowState state)
    {
        var maximized = state == WindowState.Maximized;
        MaximizeGlyph.IsVisible = !maximized;
        RestoreGlyph.IsVisible = maximized;
        ToolTip.SetTip(MaximizeButton, maximized ? "Restore" : "Maximize");
    }

    private void OnDragRegionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window is null)
            return;
        // Caption buttons handle their own clicks.
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        _window.BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        if (_window is not null)
            _window.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        if (_window is null)
            return;
        _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    /// <summary>Same path as the stock X: Closing still runs, so hide-to-tray applies.</summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e) => _window?.Close();
}
