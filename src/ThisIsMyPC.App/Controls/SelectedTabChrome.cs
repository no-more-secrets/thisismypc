using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ThisIsMyPC.App.Controls;

/// <summary>Draws a selected tab with one continuous, one-pixel outline through its concave joins.</summary>
public sealed class SelectedTabChrome : Control
{
    /// <summary>The selected tab's fill.</summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<SelectedTabChrome>();
    /// <summary>The shared outline brush.</summary>
    public static readonly StyledProperty<IBrush?> BorderBrushProperty = Border.BorderBrushProperty.AddOwner<SelectedTabChrome>();

    static SelectedTabChrome() => AffectsRender<SelectedTabChrome>(BackgroundProperty, BorderBrushProperty);

    /// <summary>Gets or sets the selected tab's fill.</summary>
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    /// <summary>Gets or sets the shared outline brush.</summary>
    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }

    private Size geometrySize;
    private StreamGeometry? outline;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var size = Bounds.Size;
        if (size.Width < 10 || size.Height < 12) return;
        if (outline is null || geometrySize != size)
        {
            geometrySize = size;
            outline = new StreamGeometry();
            using var path = outline.Open();
            // All curves and straight segments share the same stroke centerline.
            // The fillets start seven pixels above the bottom, level with the other tabs.
            path.BeginFigure(new Point(-6, size.Height - 0.5), true);
            path.ArcTo(new Point(0.5, size.Height - 7), new Size(6.5, 6.5), 0, false, SweepDirection.CounterClockwise);
            path.LineTo(new Point(0.5, 4.5));
            path.ArcTo(new Point(4.5, 0.5), new Size(4, 4), 0, false, SweepDirection.Clockwise);
            path.LineTo(new Point(size.Width - 4.5, 0.5));
            path.ArcTo(new Point(size.Width - 0.5, 4.5), new Size(4, 4), 0, false, SweepDirection.Clockwise);
            path.LineTo(new Point(size.Width - 0.5, size.Height - 7));
            path.ArcTo(new Point(size.Width + 6, size.Height - 0.5), new Size(6.5, 6.5), 0, false, SweepDirection.CounterClockwise);
            path.EndFigure(false);
        }
        // Extend the fill into the content by one DIP. At fractional DPI the
        // chrome bottom and strip padding round separately; ending exactly at
        // Bounds.Height leaves a partially covered dark pixel between them.
        context.DrawGeometry(Background, BorderBrush is { } brush ? new Pen(brush, 1) : null, outline);
        using (context.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased }))
            context.DrawRectangle(Background, null, new Rect(-6, size.Height - 1, size.Width + 12, 2));
    }
}
