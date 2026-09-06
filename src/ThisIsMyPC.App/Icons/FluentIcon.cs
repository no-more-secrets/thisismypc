using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace ThisIsMyPC.App.Icons;

/// <summary>Renders official Fluent paths on their shared 20px canvas.</summary>
public sealed class FluentIcon : Control
{
    public static readonly StyledProperty<FluentSymbol> SymbolProperty =
        AvaloniaProperty.Register<FluentIcon, FluentSymbol>(nameof(Symbol));
    public static readonly StyledProperty<bool> IsFilledProperty =
        AvaloniaProperty.Register<FluentIcon, bool>(nameof(IsFilled));
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<FluentIcon>();

    static FluentIcon()
    {
        WidthProperty.OverrideDefaultValue<FluentIcon>(20);
        HeightProperty.OverrideDefaultValue<FluentIcon>(20);
        AffectsRender<FluentIcon>(SymbolProperty, IsFilledProperty, ForegroundProperty);
    }

    public FluentSymbol Symbol
    {
        get => GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public bool IsFilled
    {
        get => GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var transform = Matrix.CreateScale(size / 20, size / 20)
            * Matrix.CreateTranslation((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using (context.PushTransform(transform))
            context.DrawGeometry(Foreground, null, FluentIconData.Get(Symbol, IsFilled));
    }
}
