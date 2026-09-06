using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.VisualTree;

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
    private double geometryBaseline;
    private double geometryThickness;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var size = Bounds.Size;
        if (size.Width < 10 || size.Height < 12) return;
        var strip = this.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Strip");
        var thickness = strip is null ? 1 : strip.UseLayoutRounding
            ? LayoutHelper.RoundLayoutThickness(strip.BorderThickness, LayoutHelper.GetLayoutScale(strip), LayoutHelper.GetLayoutScale(strip)).Bottom
            : strip.BorderThickness.Bottom;
        var bottom = size.Height;
        if (strip?.TranslatePoint(new Point(0, strip.Bounds.Height), this) is { } stripFloor
            && Math.Abs(stripFloor.Y - bottom) < 3)
            bottom = stripFloor.Y;
        // A wrapped upper-row tab stays on its own row. Only the last row
        // joins the strip floor, whose actual rounded stroke is authoritative.
        var baseline = bottom - thickness / 2;
        var inset = thickness / 2;
        var radius = 6 + inset;
        if (outline is null || geometrySize != size || geometryBaseline != baseline || geometryThickness != thickness)
        {
            geometrySize = size;
            geometryBaseline = baseline;
            geometryThickness = thickness;
            outline = new StreamGeometry();
            using var path = outline.Open();
            // All curves and straight segments share the same stroke centerline.
            // The fillets start seven pixels above the bottom, level with the other tabs.
            path.BeginFigure(new Point(-6, baseline), true);
            path.ArcTo(new Point(inset, baseline - radius), new Size(radius, radius), 0, false, SweepDirection.CounterClockwise);
            path.LineTo(new Point(inset, 4 + inset));
            path.ArcTo(new Point(4 + inset, inset), new Size(4, 4), 0, false, SweepDirection.Clockwise);
            path.LineTo(new Point(size.Width - 4 - inset, inset));
            path.ArcTo(new Point(size.Width - inset, 4 + inset), new Size(4, 4), 0, false, SweepDirection.Clockwise);
            path.LineTo(new Point(size.Width - inset, baseline - radius));
            path.ArcTo(new Point(size.Width + 6, baseline), new Size(radius, radius), 0, false, SweepDirection.CounterClockwise);
            path.EndFigure(false);
        }
        // Extend the fill into the content by one DIP. At fractional DPI the
        // chrome bottom and strip padding round separately; ending exactly at
        // Bounds.Height leaves a partially covered dark pixel between them.
        context.DrawGeometry(Background, null, outline);
        using (context.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased }))
            context.DrawRectangle(Background, null, new Rect(-6, bottom - thickness, size.Width + 12, thickness + 1));
        // Paint the outline last so the floor fill cannot erase the curved joins.
        if (BorderBrush is { } brush)
            context.DrawGeometry(null, new Pen(brush, thickness), outline);
    }
}
