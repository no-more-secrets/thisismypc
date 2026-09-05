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

    public static readonly StyledProperty<double> RowGapProperty =
        AvaloniaProperty.Register<JustifiedWrapPanel, double>(nameof(RowGap), double.NaN);

    public static readonly StyledProperty<bool> PlaceSelectedRowLastProperty =
        AvaloniaProperty.Register<JustifiedWrapPanel, bool>(nameof(PlaceSelectedRowLast));

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>Gets or sets the vertical gap. NaN uses <see cref="Gap"/>.</summary>
    public double RowGap
    {
        get => GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the row with the selected tab is arranged last.
    /// Child order stays unchanged, so keyboard navigation keeps its source order.
    /// </summary>
    public bool PlaceSelectedRowLast
    {
        get => GetValue(PlaceSelectedRowLastProperty);
        set => SetValue(PlaceSelectedRowLastProperty, value);
    }

    private double EffectiveRowGap => double.IsNaN(RowGap) ? Gap : RowGap;

    static JustifiedWrapPanel()
    {
        AffectsMeasure<JustifiedWrapPanel>(GapProperty, RowGapProperty, PlaceSelectedRowLastProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var rows = BuildRows(availableSize.Width, measure: true);
        var height = 0.0;
        var width = 0.0;
        foreach (var row in rows)
        {
            height += (height > 0 ? EffectiveRowGap : 0) + row.Height;
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
            y += row.Height + EffectiveRowGap;
        }
        return finalSize;
    }

    private sealed class Row
    {
        public List<Control> Children { get; } = [];
        public double Width { get; set; }
        public double Height { get; set; }
    }

    /// <summary>
    /// Rows by desired width. A greedy fill finds the fewest rows that hold
    /// everything; the children are then re-split into that many rows so the
    /// widest row is as narrow as possible (a linear partition), which spreads
    /// the chips evenly instead of leaving one orphan on the last row. Width
    /// is the sum of the children and the gaps between them.
    /// </summary>
    private List<Row> BuildRows(double availableWidth, bool measure)
    {
        var children = new List<Control>();
        foreach (var child in Children)
        {
            if (!child.IsVisible)
                continue;
            if (measure)
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            children.Add(child);
        }
        if (children.Count == 0)
            return [];

        var widths = children.Select(c => c.DesiredSize.Width).ToArray();
        var rowCount = double.IsInfinity(availableWidth) ? 1 : GreedyRowCount(widths, availableWidth);
        var breaks = rowCount <= 1 ? [children.Count] : EvenBreaks(widths, rowCount);

        var rows = new List<Row>();
        var from = 0;
        foreach (var to in breaks)
        {
            var row = new Row();
            for (var i = from; i < to; i++)
            {
                row.Children.Add(children[i]);
                row.Width += (row.Children.Count > 1 ? Gap : 0) + widths[i];
                row.Height = Math.Max(row.Height, children[i].DesiredSize.Height);
            }
            rows.Add(row);
            from = to;
        }

        if (PlaceSelectedRowLast && rows.Count > 1)
        {
            var selectedRow = rows.FindIndex(row => row.Children.OfType<TabItem>().Any(tab => tab.IsSelected));
            if (selectedRow >= 0 && selectedRow != rows.Count - 1)
            {
                var row = rows[selectedRow];
                rows.RemoveAt(selectedRow);
                rows.Add(row);
            }
        }

        return rows;
    }

    /// <summary>The fewest rows a first-fit fill needs; one child per row at least.</summary>
    private int GreedyRowCount(double[] widths, double availableWidth)
    {
        var rows = 1;
        var width = 0.0;
        for (var i = 0; i < widths.Length; i++)
        {
            var next = i == 0 || width == 0 ? widths[i] : width + Gap + widths[i];
            if (width > 0 && next > availableWidth)
            {
                rows++;
                width = widths[i];
            }
            else
            {
                width = next;
            }
        }
        return rows;
    }

    /// <summary>
    /// End indexes (exclusive) of <paramref name="rowCount"/> contiguous rows
    /// whose widest row is as narrow as possible. Never wider than the greedy
    /// rows, so everything that fit before still fits.
    /// </summary>
    private int[] EvenBreaks(double[] widths, int rowCount)
    {
        var n = widths.Length;
        rowCount = Math.Min(rowCount, n);
        // prefix[i] = width of children 0..i-1 laid in one row
        var prefix = new double[n + 1];
        for (var i = 0; i < n; i++)
            prefix[i + 1] = prefix[i] + widths[i] + (i > 0 ? Gap : 0);
        double Span(int from, int to) => prefix[to] - prefix[from] - (from > 0 ? Gap : 0);

        // best[k][i]: the narrowest widest-row when children 0..i-1 fill k rows
        var best = new double[rowCount + 1, n + 1];
        var cut = new int[rowCount + 1, n + 1];
        for (var i = 0; i <= n; i++)
            best[0, i] = i == 0 ? 0 : double.PositiveInfinity;
        for (var k = 1; k <= rowCount; k++)
        {
            for (var i = 0; i <= n; i++)
            {
                best[k, i] = double.PositiveInfinity;
                for (var j = k - 1; j < i; j++)
                {
                    if (double.IsInfinity(best[k - 1, j]))
                        continue;
                    var candidate = Math.Max(best[k - 1, j], Span(j, i));
                    if (candidate < best[k, i])
                    {
                        best[k, i] = candidate;
                        cut[k, i] = j;
                    }
                }
            }
        }

        var breaks = new int[rowCount];
        var end = n;
        for (var k = rowCount; k >= 1; k--)
        {
            breaks[k - 1] = end;
            end = cut[k, end];
        }
        return breaks;
    }
}
