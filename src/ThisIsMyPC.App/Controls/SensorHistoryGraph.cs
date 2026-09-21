using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ThisIsMyPC.Core.Hardware.Sensors;

namespace ThisIsMyPC.App.Controls;

/// <summary>Small read-only history graph. Missing samples split the line into separate segments.</summary>
public sealed class SensorHistoryGraph : Control
{
    public static readonly StyledProperty<IReadOnlyList<HardwareSensorSample>?> SamplesProperty =
        AvaloniaProperty.Register<SensorHistoryGraph, IReadOnlyList<HardwareSensorSample>?>(nameof(Samples));
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<SensorHistoryGraph, IBrush?>(nameof(Stroke), Brushes.SteelBlue);
    public IReadOnlyList<HardwareSensorSample>? Samples { get => GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    static SensorHistoryGraph() => AffectsRender<SensorHistoryGraph>(SamplesProperty, StrokeProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var pen = new Pen(Stroke, 1.5);
        foreach (var segment in CreateSegments(Samples ?? [], Bounds.Size))
        {
            if (segment.Length == 1) context.DrawEllipse(Stroke, null, segment[0], 1.5, 1.5);
            for (var i = 1; i < segment.Length; i++) context.DrawLine(pen, segment[i - 1], segment[i]);
        }
    }

    /// <summary>Creates separate point lists so no line crosses an unavailable reading.</summary>
    public static IReadOnlyList<Point[]> CreateSegments(IReadOnlyList<HardwareSensorSample> samples, Size size)
    {
        var retained = samples.TakeLast(120).ToArray();
        var values = retained.Where(s => s.Value is { } value && double.IsFinite(value)).Select(s => s.Value!.Value).ToArray();
        if (values.Length == 0 || size.Width < 4 || size.Height < 4) return [];
        var minimum = values.Min();
        var maximum = values.Max();
        // Normalize before subtraction: two finite readings can have an infinite difference.
        var scale = Math.Max(Math.Abs(minimum), Math.Abs(maximum));
        var normalizedMinimum = scale > 0 ? minimum / scale : 0;
        var span = scale > 0 ? maximum / scale - normalizedMinimum : 0;
        var segments = new List<Point[]>();
        var current = new List<Point>();
        for (var i = 0; i < retained.Length; i++)
        {
            if (retained[i].Value is not { } value || !double.IsFinite(value))
            {
                if (current.Count > 0) { segments.Add(current.ToArray()); current.Clear(); }
                continue;
            }
            var x = 2 + (120 - retained.Length + i) / 119d * (size.Width - 4);
            var fraction = span > 0 ? (value / scale - normalizedMinimum) / span : 0.5;
            current.Add(new(x, 2 + (1 - fraction) * (size.Height - 4)));
        }
        if (current.Count > 0) segments.Add(current.ToArray());
        return segments;
    }
}
