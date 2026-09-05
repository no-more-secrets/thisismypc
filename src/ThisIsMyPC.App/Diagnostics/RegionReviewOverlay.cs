#if DEBUG
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace ThisIsMyPC.App.Diagnostics;

internal sealed class RegionReviewOverlay : Panel
{
    private static readonly IBrush ShadeBrush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
    private static readonly IBrush FillBrush = new SolidColorBrush(Color.FromArgb(48, 255, 72, 72));
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.FromRgb(255, 72, 72));
    private static readonly IBrush FigureBrush = new SolidColorBrush(Color.FromRgb(255, 170, 64));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromArgb(245, 126, 20, 28));
    private static readonly IBrush HintBrush = new SolidColorBrush(Color.FromRgb(20, 20, 34));

    private readonly Window window;
    private readonly RegionReviewStore store;
    private readonly Func<string> pageRouteResolver;
    private readonly List<FigureState> figures = [];
    private readonly List<CaptureState> captures = [];
    private readonly DrawingPresenter drawingPresenter;
    private readonly TextBox noteEditor;
    private readonly Border editorHost;
    private readonly Dictionary<int, Button> pencilButtons = [];
    private RenderTargetBitmap? frozenFrame;
    private Point dragStart;
    private Point dragCurrent;
    private DateTime frozenCapturedAtUtc;
    private RegionReviewRecord? activeRecord;
    private bool dragging;
    private IPointer? activePointer;
    private string? failureMessage;
    private int nextFigureNumber = 1;
    private int? selectedFigureNumber;
    private CaptureState? currentCapture;

    internal RegionReviewOverlay(Window window, string? outputDirectory = null, Func<string>? pageRouteResolver = null)
        : this(window, new RegionReviewStore(outputDirectory), pageRouteResolver) { }

    internal RegionReviewOverlay(Window window, RegionReviewStore store, Func<string>? pageRouteResolver = null)
    {
        this.window = window;
        this.store = store;
        this.pageRouteResolver = pageRouteResolver ?? (() => "window");
        Focusable = true;
        IsVisible = false;
        drawingPresenter = new DrawingPresenter(this);

        noteEditor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Watermark = "Optional note for this figure",
            MinHeight = 72,
        };
        var saveButton = new Button { Content = "Save", MinWidth = 72 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 72 };
        saveButton.Click += (_, _) => SaveNote();
        cancelButton.Click += (_, _) => CancelNote();
        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { cancelButton, saveButton },
        };
        editorHost = new Border
        {
            Width = 360,
            Height = 138,
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            Background = HintBrush,
            BorderBrush = SelectedBrush,
            BorderThickness = new Thickness(1),
            IsVisible = false,
            Child = new StackPanel { Spacing = 8, Children = { noteEditor, actions } },
        };
        Children.Add(drawingPresenter);
        Children.Add(editorHost);
    }

    internal bool IsReviewActive => IsVisible;
    internal bool IsEditingNote => editorHost.IsVisible;
    internal bool CanSelect => frozenFrame is not null;
    internal Func<RenderTargetBitmap>? CaptureOverride { get; set; }
    internal Rect SelectionBounds => SelectedFigure?.Bounds ?? default;
    internal int FigureCount => figures.Count;
    internal int? SelectedFigureNumber => selectedFigureNumber;
    internal string OutputDirectory => store.OutputDirectory;

    private IEnumerable<FigureState> CurrentFigures => currentCapture is null
        ? [] : figures.Where(figure => figure.CaptureId == currentCapture.Id);
    private FigureState? SelectedFigure => selectedFigureNumber is int number
        ? figures.FirstOrDefault(figure => figure.Number == number)
        : null;

    internal void Start()
    {
        dragging = false;
        CancelNote();
        IsVisible = true;
        if (figures.Count == 0 && captures.Count == 0)
        {
            store.StartSession();
            selectedFigureNumber = null;
            nextFigureNumber = 1;
            activeRecord = null;
            if (!WriteInactiveRecord(suspended: false))
            {
                Focus();
                InvalidateVisual();
                return;
            }
        }

        failureMessage = null;
        var route = pageRouteResolver();
        currentCapture = captures.LastOrDefault(capture => capture.PageRoute == route
            && Math.Abs(capture.LogicalWidth - window.ClientSize.Width) < 0.01
            && Math.Abs(capture.LogicalHeight - window.ClientSize.Height) < 0.01
            && Math.Abs(capture.RenderScale - window.RenderScaling) < 0.001
            && figures.Any(figure => figure.CaptureId == capture.Id));
        if (currentCapture is not null)
        {
            frozenFrame = currentCapture.Frame;
            frozenCapturedAtUtc = currentCapture.CapturedAtUtc;
            selectedFigureNumber = CurrentFigures.LastOrDefault()?.Number;
            WriteExistingRecord(suspended: false);
            Focus();
            InvalidateVisual();
            return;
        }
        frozenFrame = null;
        IsVisible = false;
        try
        {
            frozenFrame = CaptureOverride?.Invoke() ?? CaptureWindow();
            frozenCapturedAtUtc = DateTime.UtcNow;
            currentCapture = new CaptureState(Guid.NewGuid().ToString("N"), route, frozenCapturedAtUtc,
                string.Empty, window.RenderScaling,
                Math.Max(1, (int)Math.Ceiling(window.ClientSize.Width * window.RenderScaling)),
                Math.Max(1, (int)Math.Ceiling(window.ClientSize.Height * window.RenderScaling)),
                window.ClientSize.Width, window.ClientSize.Height, frozenFrame);
            captures.Add(currentCapture);
            selectedFigureNumber = null;
            if (figures.Count > 0)
                WriteExistingRecord(suspended: false);
        }
        catch (Exception exception)
        {
            failureMessage = $"Region capture failed: {exception.Message}";
        }
        IsVisible = true;
        Focus();
        InvalidateVisual();
    }

    internal void Suspend()
    {
        if (!IsVisible)
            return;
        CancelDrag();
        if (IsEditingNote)
        {
            SaveNote();
            if (IsEditingNote)
                return;
        }
        if (figures.Count > 0 && !(CurrentFigures.Any()
            ? SaveCurrentState(suspended: true)
            : WriteExistingRecord(suspended: true)))
            return;
        dragging = false;
        activePointer?.Capture(null);
        activePointer = null;
        CancelNote();
        IsVisible = false;
        frozenFrame = null;
        selectedFigureNumber = null;
        if (currentCapture is not null && !figures.Any(figure => figure.CaptureId == currentCapture.Id))
        {
            currentCapture.Frame.Dispose();
            captures.Remove(currentCapture);
        }
        currentCapture = null;
    }

    internal void Reset()
    {
        if (!WriteInactiveRecord())
            return;
        SuspendFrame();
        ClearPencilButtons();
        figures.Clear();
        captures.Clear();
        nextFigureNumber = 1;
        activeRecord = null;
    }

    internal void Clear() => Reset();

    internal void Close()
    {
        if (!WriteInactiveRecord())
            return;
        SuspendFrame();
        ClearPencilButtons();
        figures.Clear();
        captures.Clear();
        activeRecord = null;
    }

    private void SuspendFrame()
    {
        dragging = false;
        activePointer?.Capture(null);
        activePointer = null;
        CancelNote();
        IsVisible = false;
        foreach (var capture in captures)
            capture.Frame.Dispose();
        frozenFrame = null;
        selectedFigureNumber = null;
        currentCapture = null;
    }

    internal void CancelNote()
    {
        if (!editorHost.IsVisible)
            return;
        editorHost.IsVisible = false;
        noteEditor.Text = string.Empty;
        Focus();
        InvalidateArrange();
        InvalidateVisual();
    }

    internal void CancelDrag()
    {
        if (!dragging)
            return;
        dragging = false;
        activePointer?.Capture(null);
        activePointer = null;
        InvalidateArrange();
        InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        drawingPresenter.Arrange(new Rect(finalSize));
        var viewport = Viewport;
        foreach (var (number, button) in pencilButtons)
        {
            var buttonFigure = CurrentFigures.FirstOrDefault(item => item.Number == number);
            button.IsVisible = buttonFigure is not null && !IsEditingNote && !dragging;
            if (buttonFigure is null)
                continue;
            var displayed = ToDisplay(buttonFigure.Bounds);
            var x = displayed.Right - 32;
            var y = displayed.Y + 4;
            var badge = BadgeBounds(buttonFigure);
            if (new Rect(x, y, 28, 28).Intersects(badge))
            {
                x = badge.Right + 4;
                y = displayed.Y;
                if (x + 28 > viewport.Right - 4)
                {
                    x = badge.X;
                    y = badge.Bottom + 4;
                }
            }
            x = Math.Clamp(x, viewport.X + 4, Math.Max(viewport.X + 4, viewport.Right - 32));
            y = Math.Clamp(y, viewport.Y + 4, Math.Max(viewport.Y + 4, viewport.Bottom - 32));
            if (new Rect(x, y, 28, 28).Intersects(badge))
            {
                var candidates = new[]
                {
                    new Point(badge.X, badge.Y - 32),
                    new Point(badge.X - 32, badge.Y),
                    new Point(badge.Right + 4, badge.Y),
                    new Point(badge.X, badge.Bottom + 4),
                };
                var fallback = candidates.FirstOrDefault(candidate =>
                    candidate.X >= viewport.X + 4 && candidate.Y >= viewport.Y + 4
                    && candidate.X + 28 <= viewport.Right - 4 && candidate.Y + 28 <= viewport.Bottom - 4
                    && !new Rect(candidate, new Size(28, 28)).Intersects(badge));
                if (fallback != default)
                {
                    x = fallback.X;
                    y = fallback.Y;
                }
            }
            button.Arrange(new Rect(x, y, 28, 28));
        }
        if (editorHost.IsVisible && SelectedFigure is { } figure)
        {
            var displayedBounds = ToDisplay(figure.Bounds);
            const double width = 360;
            const double height = 138;
            var x = Math.Clamp(displayedBounds.X, 12, Math.Max(12, finalSize.Width - width - 12));
            var preferredY = displayedBounds.Bottom + 12;
            var y = preferredY + height <= finalSize.Height - 12
                ? preferredY
                : displayedBounds.Y - height - 12;
            y = Math.Clamp(y, 60, Math.Max(60, finalSize.Height - height - 12));
            editorHost.Arrange(new Rect(x, y, width, height));
        }
        return finalSize;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        e.Handled = true;
        var isFromEditor = IsFromEditor(e.Source);
        if (isFromEditor
            || IsFromPencil(e.Source)
            || IsEditingNote
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || frozenFrame is null)
            return;

        var displayPoint = e.GetPosition(this);
        if (!Viewport.Contains(displayPoint))
            return;
        var point = ToCapture(displayPoint);
        var hitFigure = CurrentFigures.LastOrDefault(figure => BadgeBounds(figure).Contains(displayPoint));
        if (hitFigure is not null)
        {
            SelectFigure(hitFigure.Number);
            return;
        }

        dragStart = point;
        dragCurrent = point;
        dragging = true;
        activePointer = e.Pointer;
        failureMessage = null;
        e.Pointer.Capture(this);
        InvalidateArrange();
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        e.Handled = true;
        if (!dragging)
            return;
        dragCurrent = ToCapture(e.GetPosition(this));
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        e.Handled = true;
        if (!dragging)
            return;
        dragCurrent = ToCapture(e.GetPosition(this));
        var candidate = Normalize(dragStart, dragCurrent);
        dragging = false;
        e.Pointer.Capture(null);
        activePointer = null;
        InvalidateArrange();
        if (candidate.Width < 2 || candidate.Height < 2)
        {
            InvalidateVisual();
            return;
        }

        if (currentCapture is null)
            return;
        var figure = new FigureState(nextFigureNumber++, Guid.NewGuid().ToString("N"), candidate, null,
            currentCapture.Id, currentCapture.PageRoute, currentCapture.CapturedAtUtc, currentCapture.ImagePath);
        var previousSelection = selectedFigureNumber;
        figures.Add(figure);
        AddPencilButton(figure);
        selectedFigureNumber = figure.Number;
        if (!SaveCurrentState())
        {
            figures.Remove(figure);
            RemovePencilButton(figure.Number);
            selectedFigureNumber = previousSelection;
            nextFigureNumber--;
            RestoreActiveRecord();
        }
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) => e.Handled = true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (IsEditingNote)
                CancelNote();
            else
                Suspend();
        }
        else if (!IsEditingNote && e.Key == Key.N)
            EditSelectedNote();
        else if (!IsEditingNote && e.Key == Key.Delete)
            DeleteSelectedFigure();
        e.Handled = true;
    }

    private void RenderOverlay(DrawingContext context)
    {
        var viewport = Viewport;
        context.FillRectangle(HintBrush, Bounds);
        if (frozenFrame is not null)
        {
            context.DrawImage(frozenFrame,
                new Rect(0, 0, frozenFrame.PixelSize.Width, frozenFrame.PixelSize.Height),
                viewport);
        }
        var currentFigures = CurrentFigures.ToArray();
        if (dragging || currentFigures.Length == 0)
            context.FillRectangle(ShadeBrush, Bounds);
        foreach (var figure in currentFigures)
            DrawFigure(context, figure);
        if (dragging)
        {
            var selection = ToDisplay(Normalize(dragStart, dragCurrent));
            context.FillRectangle(FillBrush, selection);
            context.DrawRectangle(new Pen(SelectedBrush, 3), selection);
        }

        if (!string.IsNullOrEmpty(failureMessage))
        {
            context.FillRectangle(ErrorBrush, new Rect(0, 0, Bounds.Width, 44));
            DrawText(context, failureMessage, new Point(14, 12));
        }
    }

    private void DrawFigure(DrawingContext context, FigureState figure)
    {
        var isSelected = figure.Number == selectedFigureNumber;
        var brush = isSelected ? SelectedBrush : FigureBrush;
        context.DrawRectangle(new Pen(brush, isSelected ? 3 : 2), ToDisplay(figure.Bounds));
        var badge = BadgeBounds(figure);
        context.FillRectangle(brush, badge, 12);
        DrawText(context, figure.Number.ToString(CultureInfo.InvariantCulture),
            new Point(badge.X + (figure.Number < 10 ? 8 : 4), badge.Y + 3));
    }

    private void DrawFigureAt(DrawingContext context, FigureState figure, Rect bounds, Size size)
    {
        var isSelected = figure.Number == selectedFigureNumber;
        var brush = isSelected ? SelectedBrush : FigureBrush;
        context.DrawRectangle(new Pen(brush, isSelected ? 3 : 2), bounds);
        var badge = new Rect(
            Math.Clamp(bounds.X - 12, 4, Math.Max(4, size.Width - 28)),
            Math.Clamp(bounds.Y - 12, 54, Math.Max(54, size.Height - 28)), 24, 24);
        context.FillRectangle(brush, badge, 12);
        DrawText(context, figure.Number.ToString(CultureInfo.InvariantCulture),
            new Point(badge.X + (figure.Number < 10 ? 8 : 4), badge.Y + 3));
    }

    private void SelectFigure(int number)
    {
        if (selectedFigureNumber == number)
            return;
        var previous = selectedFigureNumber;
        selectedFigureNumber = number;
        if (!SaveCurrentState())
        {
            selectedFigureNumber = previous;
            RestoreActiveRecord();
        }
        InvalidateVisual();
    }

    internal void EditSelectedNote()
    {
        if (SelectedFigure is not { } figure)
            return;
        noteEditor.Text = figure.Note ?? string.Empty;
        editorHost.IsVisible = true;
        InvalidateArrange();
        InvalidateVisual();
        noteEditor.Focus();
        noteEditor.CaretIndex = noteEditor.Text?.Length ?? 0;
    }

    private void SaveNote()
    {
        if (SelectedFigure is not { } figure)
        {
            CancelNote();
            return;
        }
        var previousNote = figure.Note;
        figure.Note = string.IsNullOrWhiteSpace(noteEditor.Text) ? null : noteEditor.Text.Trim();
        editorHost.IsVisible = false;
        if (!SaveCurrentState())
        {
            figure.Note = previousNote;
            RestoreActiveRecord();
            editorHost.IsVisible = true;
            noteEditor.Focus();
        }
        else
        {
            noteEditor.Text = string.Empty;
            Focus();
        }
        InvalidateArrange();
        InvalidateVisual();
    }

    internal void DeleteSelectedFigure()
    {
        if (SelectedFigure is not { } figure)
            return;
        var index = figures.IndexOf(figure);
        var previousSelection = selectedFigureNumber;
        figures.RemoveAt(index);
        RemovePencilButton(figure.Number);
        var remainingCurrent = CurrentFigures.ToArray();
        selectedFigureNumber = remainingCurrent.Length == 0 ? null : remainingCurrent[^1].Number;
        var saved = figures.Count == 0 ? WriteInactiveRecord(suspended: false)
            : remainingCurrent.Length == 0 ? WriteExistingRecord(suspended: false)
            : SaveCurrentState();
        if (!saved)
        {
            figures.Insert(index, figure);
            AddPencilButton(figure);
            selectedFigureNumber = previousSelection;
            RestoreActiveRecord();
        }
        else if (figures.Count == 0)
            activeRecord = null;
        InvalidateVisual();
    }

    private bool SaveCurrentState(bool suspended = false)
    {
        var selected = SelectedFigure ?? figures.LastOrDefault();
        if (selected is null || currentCapture is null)
            return figures.Count == 0;
        try
        {
            Directory.CreateDirectory(store.OutputDirectory);
            failureMessage = null;
            var imagePath = Path.GetFullPath(Path.Combine(
                store.OutputDirectory,
                $"region-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.png"));
            using var annotatedFrame = CaptureOverlay();
            annotatedFrame.Save(imagePath);
            var figureRecords = figures.Select(figure => figure.CaptureId == currentCapture.Id
                ? ToRecord(figure, imagePath) : ToRecord(figure)).ToArray();
            var referencedCaptureIds = figureRecords.Select(figure => figure.CaptureId).ToHashSet(StringComparer.Ordinal);
            var captureRecords = captures.Where(capture => referencedCaptureIds.Contains(capture.Id))
                .Select(capture => capture.Id == currentCapture.Id
                    ? ToRecord(capture, imagePath, annotatedFrame.PixelSize.Width, annotatedFrame.PixelSize.Height)
                    : ToRecord(capture)).ToArray();
            var record = store.CreateRecord(
                true,
                selected.Id,
                frozenCapturedAtUtc,
                window.Title ?? string.Empty,
                ToBounds(selected.Bounds),
                currentCapture.RenderScale,
                annotatedFrame.PixelSize.Width,
                annotatedFrame.PixelSize.Height,
                imagePath,
                selectedFigureNumber ?? selected.Number,
                figureRecords,
                captureRecords,
                suspended);
            store.Write(record);
            currentCapture.ImagePath = imagePath;
            currentCapture.PixelWidth = annotatedFrame.PixelSize.Width;
            currentCapture.PixelHeight = annotatedFrame.PixelSize.Height;
            foreach (var figure in CurrentFigures)
                figure.ImagePath = imagePath;
            activeRecord = record;
            return true;
        }
        catch (Exception exception)
        {
            failureMessage = $"Region capture failed: {exception.Message}";
            return false;
        }
    }

    private bool WriteExistingRecord(bool suspended)
    {
        var selected = SelectedFigure ?? figures.LastOrDefault();
        var capture = selected is null ? null : captures.FirstOrDefault(item => item.Id == selected.CaptureId);
        if (selected is null || capture is null)
            return true;
        try
        {
            var referencedCaptureIds = figures.Select(figure => figure.CaptureId).ToHashSet(StringComparer.Ordinal);
            var record = store.CreateRecord(true, selected.Id, selected.CapturedAtUtc,
                window.Title ?? string.Empty, ToBounds(selected.Bounds), capture.RenderScale,
                capture.PixelWidth, capture.PixelHeight, selected.ImagePath, selected.Number,
                figures.Select(figure => ToRecord(figure)).ToArray(),
                captures.Where(item => referencedCaptureIds.Contains(item.Id))
                    .Select(item => ToRecord(item)).ToArray(), suspended);
            store.Write(record);
            activeRecord = record;
            return true;
        }
        catch (Exception exception)
        {
            failureMessage = $"Region review state failed: {exception.Message}";
            return false;
        }
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
        if (currentCapture is null)
            throw new InvalidOperationException("No region review capture is active.");
        var size = new Size(currentCapture.LogicalWidth, currentCapture.LogicalHeight);
        var presenter = new ExportPresenter(this, size);
        presenter.Measure(size);
        presenter.Arrange(new Rect(size));
        var bitmap = new RenderTargetBitmap(
            new PixelSize(currentCapture.PixelWidth, currentCapture.PixelHeight),
            new Vector(96 * currentCapture.RenderScale, 96 * currentCapture.RenderScale));
        bitmap.Render(presenter);
        return bitmap;
    }

    private bool WriteInactiveRecord(bool suspended = true)
    {
        try
        {
            store.Write(store.CreateRecord(
                false,
                string.Empty,
                DateTime.UtcNow,
                window.Title ?? string.Empty,
                new RegionReviewBounds { X = 0, Y = 0, Width = 0, Height = 0 },
                window.RenderScaling,
                0,
                0,
                string.Empty,
                null,
                [],
                [],
                suspended));
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

    private bool IsFromEditor(object? source) => source is Visual visual
        && (ReferenceEquals(visual, editorHost) || visual.GetVisualAncestors().Contains(editorHost));

    private bool IsFromPencil(object? source) => source is Visual visual
        && pencilButtons.Values.Any(button => ReferenceEquals(visual, button)
            || visual.GetVisualAncestors().Contains(button));

    private void AddPencilButton(FigureState figure)
    {
        var button = new Button
        {
            Name = $"EditFigure{figure.Number}Note",
            Width = 28,
            Height = 28,
            Padding = new Thickness(5),
            Content = new PathIcon
            {
                Data = Geometry.Parse("M3,17.25V21h3.75L17.81,9.94l-3.75-3.75L3,17.25M20.71,7.04a.996.996 0 0 0 0-1.41l-2.34-2.34a.996.996 0 0 0-1.41,0l-1.83,1.83 3.75,3.75 1.83-1.83Z"),
            },
        };
        AutomationProperties.SetName(button, $"Edit note for figure {figure.Number}");
        ToolTip.SetTip(button, $"Edit note for figure {figure.Number}");
        button.Click += (_, _) =>
        {
            if (IsEditingNote || currentCapture?.Id != figure.CaptureId)
                return;
            SelectFigure(figure.Number);
            if (selectedFigureNumber != figure.Number)
                return;
            EditSelectedNote();
        };
        pencilButtons.Add(figure.Number, button);
        Children.Insert(Math.Max(1, Children.Count - 1), button);
        InvalidateArrange();
    }

    private void RemovePencilButton(int number)
    {
        if (!pencilButtons.Remove(number, out var button))
            return;
        Children.Remove(button);
        InvalidateArrange();
    }

    private void ClearPencilButtons()
    {
        foreach (var button in pencilButtons.Values)
            Children.Remove(button);
        pencilButtons.Clear();
    }

    private Rect BadgeBounds(FigureState figure)
    {
        var displayed = ToDisplay(figure.Bounds);
        var x = Math.Clamp(displayed.X - 12, 4, Math.Max(4, Bounds.Width - 28));
        var y = Math.Clamp(displayed.Y - 12, 54, Math.Max(54, Bounds.Height - 28));
        return new Rect(x, y, 24, 24);
    }

    private Rect Viewport
    {
        get
        {
            if (currentCapture is null)
                return new Rect(Bounds.Size);
            var scale = Math.Min(Bounds.Width / currentCapture.LogicalWidth,
                Bounds.Height / currentCapture.LogicalHeight);
            var width = currentCapture.LogicalWidth * scale;
            var height = currentCapture.LogicalHeight * scale;
            return new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
        }
    }

    private Point ToCapture(Point point)
    {
        if (currentCapture is null)
            return point;
        var viewport = Viewport;
        return new Point(
            Math.Clamp((point.X - viewport.X) * currentCapture.LogicalWidth / viewport.Width, 0, currentCapture.LogicalWidth),
            Math.Clamp((point.Y - viewport.Y) * currentCapture.LogicalHeight / viewport.Height, 0, currentCapture.LogicalHeight));
    }

    private Rect ToCapture(Rect rect)
    {
        var topLeft = ToCapture(rect.TopLeft);
        var bottomRight = ToCapture(rect.BottomRight);
        return new Rect(topLeft, bottomRight);
    }

    private Rect ToDisplay(Rect rect)
    {
        if (currentCapture is null)
            return rect;
        var viewport = Viewport;
        return new Rect(
            viewport.X + rect.X * viewport.Width / currentCapture.LogicalWidth,
            viewport.Y + rect.Y * viewport.Height / currentCapture.LogicalHeight,
            rect.Width * viewport.Width / currentCapture.LogicalWidth,
            rect.Height * viewport.Height / currentCapture.LogicalHeight);
    }

    private static Rect Normalize(Point first, Point second) => new(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Abs(second.X - first.X),
        Math.Abs(second.Y - first.Y));

    private static RegionReviewBounds ToBounds(Rect bounds) => new()
    {
        X = bounds.X,
        Y = bounds.Y,
        Width = bounds.Width,
        Height = bounds.Height,
    };

    private static RegionReviewFigure ToRecord(FigureState figure, string? imagePath = null) => new()
    {
        Number = figure.Number,
        Id = figure.Id,
        Bounds = ToBounds(figure.Bounds),
        Note = figure.Note,
        PageRoute = figure.PageRoute,
        CaptureId = figure.CaptureId,
        CapturedAtUtc = figure.CapturedAtUtc,
        ImagePath = imagePath ?? figure.ImagePath,
    };

    private static RegionReviewCapture ToRecord(CaptureState capture, string? imagePath = null,
        int? pixelWidth = null, int? pixelHeight = null) => new()
    {
        Id = capture.Id,
        PageRoute = capture.PageRoute,
        CapturedAtUtc = capture.CapturedAtUtc,
        ImagePath = imagePath ?? capture.ImagePath,
        RenderScale = capture.RenderScale,
        PixelWidth = pixelWidth ?? capture.PixelWidth,
        PixelHeight = pixelHeight ?? capture.PixelHeight,
        LogicalWidth = capture.LogicalWidth,
        LogicalHeight = capture.LogicalHeight,
    };

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

    private new void InvalidateVisual()
    {
        drawingPresenter.InvalidateVisual();
        base.InvalidateVisual();
    }

    private sealed class DrawingPresenter(RegionReviewOverlay owner) : Control
    {
        public override void Render(DrawingContext context) => owner.RenderOverlay(context);
    }

    private sealed class ExportPresenter(RegionReviewOverlay owner, Size size) : Control
    {
        public override void Render(DrawingContext context)
        {
            context.DrawImage(owner.frozenFrame!,
                new Rect(0, 0, owner.frozenFrame!.PixelSize.Width, owner.frozenFrame.PixelSize.Height),
                new Rect(size));
            foreach (var figure in owner.CurrentFigures)
                owner.DrawFigureAt(context, figure, figure.Bounds, size);
        }
    }

    private sealed class FigureState(int number, string id, Rect bounds, string? note,
        string captureId, string pageRoute, DateTime capturedAtUtc, string imagePath)
    {
        internal int Number { get; } = number;
        internal string Id { get; } = id;
        internal Rect Bounds { get; } = bounds;
        internal string? Note { get; set; } = note;
        internal string CaptureId { get; } = captureId;
        internal string PageRoute { get; } = pageRoute;
        internal DateTime CapturedAtUtc { get; } = capturedAtUtc;
        internal string ImagePath { get; set; } = imagePath;
    }

    private sealed class CaptureState(string id, string pageRoute, DateTime capturedAtUtc,
        string imagePath, double renderScale, int pixelWidth, int pixelHeight,
        double logicalWidth, double logicalHeight, RenderTargetBitmap frame)
    {
        internal string Id { get; } = id;
        internal string PageRoute { get; } = pageRoute;
        internal DateTime CapturedAtUtc { get; } = capturedAtUtc;
        internal string ImagePath { get; set; } = imagePath;
        internal double RenderScale { get; } = renderScale;
        internal int PixelWidth { get; set; } = pixelWidth;
        internal int PixelHeight { get; set; } = pixelHeight;
        internal double LogicalWidth { get; } = logicalWidth;
        internal double LogicalHeight { get; } = logicalHeight;
        internal RenderTargetBitmap Frame { get; } = frame;
    }
}
#endif
