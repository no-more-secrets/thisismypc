using Avalonia;
using Avalonia.Controls;

namespace ThisIsMyPC.App.Controls;

/// <summary>
/// Wraps children into rows by their desired widths, then stretches every
/// row to the full width by giving each child in it an equal share of the
/// leftover, so a multi-row strip (Startup &amp; Services' fourteen tabs)
/// reads as even rows instead of a ragged right edge. Gap separates
/// children in a row and rows from each other; children keep no margins of
/// their own. Under an infinite width nothing wraps or stretches.
/// </summary>
public sealed class JustifiedWrapPanel : Panel
{
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<JustifiedWrapPanel, double>(nameof(Gap), 8);

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    static JustifiedWrapPanel()
    {
        AffectsMeasure<JustifiedWrapPanel>(GapProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var rows = BuildRows(availableSize.Width, measure: true);
        var height = 0.0;
        var width = 0.0;
        foreach (var row in rows)
        {
            height += (height > 0 ? Gap : 0) + row.Height;
            width = Math.Max(width, row.Width);
        }
        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0.0;
        foreach (var row in BuildRows(finalSize.Width, measure: false))
        {
            var count = row.Children.Count;
            var extra = double.IsInfinity(finalSize.Width) ? 0 : Math.Max(0, finalSize.Width - row.Width) / count;
            var x = 0.0;
            foreach (var child in row.Children)
            {
                var width = child.DesiredSize.Width + extra;
                child.Arrange(new Rect(x, y, width, row.Height));
                x += width + Gap;
            }
            y += row.Height + Gap;
        }
        return finalSize;
    }

    private sealed class Row
    {
        public List<Control> Children { get; } = [];
        public double Width { get; set; }
        public double Height { get; set; }
    }

    /// <summary>Rows by desired width; Width is the sum of children and the gaps between them.</summary>
    private List<Row> BuildRows(double availableWidth, bool measure)
    {
        var rows = new List<Row>();
        var current = new Row();
        foreach (var child in Children)
        {
            if (!child.IsVisible)
                continue;
            if (measure)
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = child.DesiredSize;
            var widthIfAdded = current.Children.Count == 0 ? size.Width : current.Width + Gap + size.Width;
            if (current.Children.Count > 0 && widthIfAdded > availableWidth)
            {
                rows.Add(current);
                current = new Row();
                widthIfAdded = size.Width;
            }
            current.Children.Add(child);
            current.Width = widthIfAdded;
            current.Height = Math.Max(current.Height, size.Height);
        }
        if (current.Children.Count > 0)
            rows.Add(current);
        return rows;
    }
}
