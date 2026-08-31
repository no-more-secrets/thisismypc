using Avalonia;
using Avalonia.Controls;

namespace ThisIsMyPC.App.Controls;

/// <summary>
/// Lays children out in as many equal-width columns as fit MinColumnWidth,
/// row by row; every card in a row is stretched to the row's tallest card so
/// rows read as clean bands. Children that measure to zero height (search-
/// filtered cards) don't consume a slot. One column below MinColumnWidth,
/// so narrow windows keep the classic stacked list.
/// </summary>
public sealed class ResponsiveColumnsPanel : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<ResponsiveColumnsPanel, double>(nameof(MinColumnWidth), 440);

    public static readonly StyledProperty<double> ColumnGapProperty =
        AvaloniaProperty.Register<ResponsiveColumnsPanel, double>(nameof(ColumnGap), 8);

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double ColumnGap
    {
        get => GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    static ResponsiveColumnsPanel()
    {
        AffectsMeasure<ResponsiveColumnsPanel>(MinColumnWidthProperty, ColumnGapProperty);
    }

    private (int Columns, double ColumnWidth) Grid(double width)
    {
        var columns = Math.Max(1, (int)(width / Math.Max(1, MinColumnWidth)));
        var columnWidth = (width - ColumnGap * (columns - 1)) / columns;
        return (columns, columnWidth);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinColumnWidth : availableSize.Width;
        var (columns, columnWidth) = Grid(width);

        double totalHeight = 0, rowHeight = 0;
        var column = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            if (child.DesiredSize.Height <= 0)
                continue;

            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (++column == columns)
            {
                totalHeight += rowHeight;
                rowHeight = 0;
                column = 0;
            }
        }

        return new Size(width, totalHeight + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, columnWidth) = Grid(finalSize.Width);

        // Chunk visible children into rows first so each row's height is known
        // before arranging (cards stretch to the tallest in their row).
        var rows = new List<List<Avalonia.Layout.Layoutable>>();
        var current = new List<Avalonia.Layout.Layoutable>();
        foreach (var child in Children)
        {
            if (child.DesiredSize.Height <= 0)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            current.Add(child);
            if (current.Count == columns)
            {
                rows.Add(current);
                current = [];
            }
        }

        if (current.Count > 0)
            rows.Add(current);

        double y = 0;
        foreach (var row in rows)
        {
            var rowHeight = row.Max(c => c.DesiredSize.Height);
            for (var i = 0; i < row.Count; i++)
                row[i].Arrange(new Rect(i * (columnWidth + ColumnGap), y, columnWidth, rowHeight));
            y += rowHeight;
        }

        return finalSize;
    }
}
