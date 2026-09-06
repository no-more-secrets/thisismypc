using Avalonia;
using Avalonia.Controls;

namespace ThisIsMyPC.App.Controls;

/// <summary>Card-edge tabs with shared tools below the strip and an optional search results view.</summary>
public class ModuleTabControl : TabControl
{
    public static readonly StyledProperty<object?> ToolbarProperty =
        AvaloniaProperty.Register<ModuleTabControl, object?>(nameof(Toolbar));
    public static readonly StyledProperty<bool> ShowTabsProperty =
        AvaloniaProperty.Register<ModuleTabControl, bool>(nameof(ShowTabs), true);
    public static readonly StyledProperty<object?> AlternateContentProperty =
        AvaloniaProperty.Register<ModuleTabControl, object?>(nameof(AlternateContent));

    public object? Toolbar { get => GetValue(ToolbarProperty); set => SetValue(ToolbarProperty, value); }
    public bool ShowTabs { get => GetValue(ShowTabsProperty); set => SetValue(ShowTabsProperty, value); }
    public object? AlternateContent { get => GetValue(AlternateContentProperty); set => SetValue(AlternateContentProperty, value); }
}
