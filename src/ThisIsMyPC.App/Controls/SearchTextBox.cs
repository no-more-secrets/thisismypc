using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using ThisIsMyPC.App.Icons;

namespace ThisIsMyPC.App.Controls;

/// <summary>A search field with an accessible clear button that preserves its text binding.</summary>
public sealed class SearchTextBox : TextBox
{
    private readonly Button _clearButton;
    protected override Type StyleKeyOverride => typeof(TextBox);

    public SearchTextBox()
    {
        var icon = new FluentIcon { Symbol = FluentSymbol.Dismiss, Width = 16, Height = 16 };
        _clearButton = new Button
        {
            Content = icon, Width = 28, Height = 28, MinWidth = 28, MinHeight = 28,
            Padding = new Thickness(0), Margin = new Thickness(2, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), IsVisible = false,
        };
        _clearButton.Classes.Add("bar-icon");
        icon.Bind(FluentIcon.ForegroundProperty, new Binding(nameof(Foreground)) { Source = _clearButton });
        AutomationProperties.SetName(_clearButton, "Clear search");
        ToolTip.SetTip(_clearButton, "Clear search");
        _clearButton.Click += (_, _) =>
        {
            if (IsReadOnly) return;
            SetCurrentValue(TextProperty, string.Empty);
            Focus();
        };
        // Keep the trailing lane measured when empty, including inside tab toolbars.
        InnerRightContent = new Border { Width = 34, Child = _clearButton };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_clearButton is not null && (change.Property == TextProperty || change.Property == IsReadOnlyProperty))
            _clearButton.IsVisible = !IsReadOnly && !string.IsNullOrEmpty(Text);
    }
}
