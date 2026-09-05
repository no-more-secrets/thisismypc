#if DEBUG
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Globalization;

namespace ThisIsMyPC.App.Diagnostics;

internal sealed class RegionReviewOverlay : Control
{
    private static readonly IBrush ShadeBrush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
    private static readonly IBrush SelectionFillBrush = new SolidColorBrush(Color.FromArgb(48, 255, 72, 72));
    private static readonly IPen SelectionPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 72, 72)), 3);
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromArgb(230, 126, 20, 28));
    private static readonly IBrush HintBrush = new SolidColorBrush(Color.FromArgb(245, 20, 20, 34));
    private static readonly IBrush BadgeBrush = new SolidColorBrush(Color.FromRgb(255, 72, 72));

    private readonly Window window;
    private readonly RegionReviewStore store;
    private RenderTargetBitmap? frozenFrame;
    private Point dragStart;
    private Point dragCurrent;
    private Rect savedSelection;
    private DateTime frozenCapturedAtUtc;
    private RegionReviewRecord? activeRecord;
    private bool dragging;
    private IPointer? activePointer;
    private string? failureMessage;

    internal RegionReviewOverlay(Window window, string? outputDirectory = null)
        : this(window, new RegionReviewStore(outputDirectory))
    {
    }

    internal RegionReviewOverlay(Window window, RegionReviewStore store)
    {
        this.window = window;
        this.store = store;
        Focusable = true;
        IsVisible = false;
    }

    internal bool IsReviewActive => IsVisible;
    internal bool CanSelect => frozenFrame is not null;
    internal Func<RenderTargetBitmap>? CaptureOverride { get; set; }
    internal Rect SelectionBounds => dragging ? Normalize(dragStart, dragCurrent) : savedSelection;
    internal string OutputDirectory => store.OutputDirectory;

    internal void Start()
    {
        dragging = false;
        IsVisible = true;
        if (!WriteInactiveRecord())
        {
            Focus();
            InvalidateVisual();
            return;
        }

        failureMessage = null;
        frozenFrame?.Dispose();
        frozenFrame = null;
        dragStart = default;
        dragCurrent = default;
        savedSelection = default;
        activeRecord = null;
        IsVisible = false;
        try
        {
            frozenFrame = CaptureOverride?.Invoke() ?? CaptureWindow();
            frozenCapturedAtUtc = DateTime.UtcNow;
        }
        catch (Exception exception)
        {
            failureMessage = $"Region capture failed: {exception.Message}";
        }
        IsVisible = true;
        Focus();
        InvalidateVisual();
    }

    internal void Clear()
    {
        if (!IsVisible)
            return;

        if (!WriteInactiveRecord())
            return;

        dragging = false;
        activePointer?.Capture(null);
        activePointer = null;
        IsVisible = false;
        frozenFrame?.Dispose();
        frozenFrame = null;
        activeRecord = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (frozenFrame is null)
            return;
        if (!WriteInactiveRecord())
            return;

        dragStart = Clamp(e.GetPosition(this));
        dragCurrent = dragStart;
        dragging = true;
        activePointer = e.Pointer;
        failureMessage = null;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        e.Handled = true;
        if (!dragging)
            return;

        dragCurrent = Clamp(e.GetPosition(this));
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        e.Handled = true;
        if (!dragging)
            return;

        dragCurrent = Clamp(e.GetPosition(this));
        var candidate = Normalize(dragStart, dragCurrent);
        dragging = false;
        e.Pointer.Capture(null);
        activePointer = null;

        if (candidate.Width < 2 || candidate.Height < 2)
        {
            RestoreActiveRecord();
            InvalidateVisual();
            return;
        }

        var previousSelection = savedSelection;
        savedSelection = candidate;
        InvalidateVisual();

        try
        {
            SaveSelection();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            savedSelection = previousSelection;
            RestoreActiveRecord();
            failureMessage = $"Region capture failed: {exception.Message}";
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) => e.Handled = true;

    internal void CancelDrag()
    {
        if (!dragging)
            return;

        dragging = false;
        activePointer?.Capture(null);
        activePointer = null;
        RestoreActiveRecord();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Clear();
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        if (frozenFrame is not null)
            context.DrawImage(frozenFrame, new Rect(frozenFrame.Size), Bounds);

        if (dragging || savedSelection.Width == 0)
            context.FillRectangle(ShadeBrush, Bounds);

        var selection = SelectionBounds;
        if (selection.Width > 0 && selection.Height > 0)
        {
            if (dragging)
                context.FillRectangle(SelectionFillBrush, selection);
            context.DrawRectangle(SelectionPen, selection);
        }

        if (!string.IsNullOrEmpty(failureMessage))
        {
            context.FillRectangle(ErrorBrush, new Rect(0, 0, Bounds.Width, 44));
            DrawText(context, failureMessage, new Point(14, 12));
        }
        else
        {
            var hint = savedSelection.Width > 0
                ? "Selection 1 is ready. Drag to replace it, or press Escape to clear."
                : "Drag around any visual area. Press Escape to clear.";
            context.FillRectangle(HintBrush, new Rect(12, 12, Math.Min(520, Bounds.Width - 24), 38), 8);
            DrawText(context, hint, new Point(24, 22));
            if (savedSelection.Width > 0)
            {
                var badgeX = Math.Clamp(savedSelection.X - 12, 4, Math.Max(4, Bounds.Width - 28));
                var badgeY = Math.Clamp(savedSelection.Y - 12, 54, Math.Max(54, Bounds.Height - 28));
                context.FillRectangle(BadgeBrush, new Rect(badgeX, badgeY, 24, 24), 12);
                DrawText(context, "1", new Point(badgeX + 8, badgeY + 3));
            }
        }
    }

    private void SaveSelection()
    {
        var selectionId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(store.OutputDirectory);
        var imagePath = Path.GetFullPath(Path.Combine(
            store.OutputDirectory,
            $"region-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{selectionId}.png"));

        using var annotatedFrame = CaptureOverlay();
        annotatedFrame.Save(imagePath);

        var record = store.CreateRecord(
            active: true,
            selectionId,
            frozenCapturedAtUtc,
            window.Title ?? string.Empty,
            new RegionReviewBounds
            {
                X = SelectionBounds.X,
                Y = SelectionBounds.Y,
                Width = SelectionBounds.Width,
                Height = SelectionBounds.Height,
            },
            window.RenderScaling,
            annotatedFrame.PixelSize.Width,
            annotatedFrame.PixelSize.Height,
            imagePath);
        store.Write(record);
        activeRecord = record;
    }

    private RenderTargetBitmap CaptureWindow()
    {
        var scale = window.RenderScaling;
        var pixelSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(window.ClientSize.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(window.ClientSize.Height * scale)));
        var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        return bitmap;
    }

    private RenderTargetBitmap CaptureOverlay()
    {
        var scale = window.RenderScaling;
        var pixelSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(Bounds.Height * scale)));
        var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scale, 96 * scale));
        bitmap.Render(this);
        return bitmap;
    }

    private bool WriteInactiveRecord()
    {
        try
        {
            store.Write(store.CreateRecord(
                active: false,
                selectionId: string.Empty,
                DateTime.UtcNow,
                window.Title ?? string.Empty,
                new RegionReviewBounds { X = 0, Y = 0, Width = 0, Height = 0 },
                window.RenderScaling,
                0,
                0,
                string.Empty));
            return true;
        }
        catch (Exception exception)
        {
            failureMessage = $"Region review state failed: {exception.Message}";
            IsVisible = true;
            InvalidateVisual();
            return false;
        }
    }

    private void RestoreActiveRecord()
    {
        if (activeRecord is null)
            return;

        try
        {
            store.Write(activeRecord);
        }
        catch (Exception exception)
        {
            failureMessage = $"Region review state failed: {exception.Message}";
        }
    }

    private Point Clamp(Point point) => new(
        Math.Clamp(point.X, 0, Bounds.Width),
        Math.Clamp(point.Y, 0, Bounds.Height));

    private static Rect Normalize(Point first, Point second) => new(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Abs(second.X - first.X),
        Math.Abs(second.Y - first.Y));

    private static void DrawText(DrawingContext context, string text, Point origin)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            14,
            Brushes.White);
        context.DrawText(formattedText, origin);
    }
}
#endif
