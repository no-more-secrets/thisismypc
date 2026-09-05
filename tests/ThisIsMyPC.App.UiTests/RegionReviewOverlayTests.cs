#if DEBUG
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThisIsMyPC.App.Diagnostics;

namespace ThisIsMyPC.App.UiTests;

public sealed class RegionReviewOverlayTests
{
    private sealed class FaultingRegionReviewStore(string outputDirectory) : RegionReviewStore(outputDirectory)
    {
        internal bool FailWrites { get; set; }

        internal override void Write(RegionReviewRecord record)
        {
            if (FailWrites)
                throw new IOException("Injected write failure.");
            base.Write(record);
        }
    }

    [AvaloniaFact]
    public void BottomRightPencil_AvoidsBadgeAndOpensEditor()
    {
        var root = new Grid();
        using var session = UiSession.ForView(root, new object(), "region-review-pencil-edge", 640, 420);
        var overlay = new RegionReviewOverlay(session.Window,
            Path.Combine(session.ShotDirectory, "records"));
        root.Children.Add(overlay);
        session.Pump();
        overlay.Start();
        session.Pump();

        Drag(session, new Point(619, 399), new Point(639, 419));
        var pencil = session.Find<Button>(button => button.Name == "EditFigure1Note");
        var badge = new Rect(607, 387, 24, 24);
        Assert.False(pencil.Bounds.Intersects(badge));
        session.Screenshot("bottom-right-pencil");
        session.Click(pencil);
        session.Pump();

        Assert.True(overlay.IsEditingNote);
        Assert.Equal(1, overlay.SelectedFigureNumber);
        Assert.Equal(1, overlay.FigureCount);
    }

    [AvaloniaFact]
    [Trait("Category", "Diagnostic")]
    public void MainWindow_OverlaySpansBodyAndCapturesBodyDrag()
    {
        using var session = UiSession.ForMainWindow("region-review-main-window");
        var mainWindow = Assert.IsType<Views.MainWindow>(session.Window);
        var originalWidth = mainWindow.Width;
        var outputDirectory = Path.Combine(session.ShotDirectory, "records");
        mainWindow.StartRegionReview(outputDirectory);
        session.Pump();

        var overlay = Assert.IsType<RegionReviewOverlay>(mainWindow.RegionReviewOverlay);
        Assert.Equal(session.Window.ClientSize.Width, overlay.Bounds.Width);
        Assert.Equal(session.Window.ClientSize.Height, overlay.Bounds.Height);
        session.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        session.Pump();
        Assert.False(overlay.IsReviewActive);
        session.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control | RawInputModifiers.Shift);
        session.Pump();
        Assert.True(overlay.IsReviewActive);

        var from = new Point(340, 150);
        var to = new Point(940, 650);
        session.Window.MouseMove(from);
        session.Window.MouseDown(from, MouseButton.Left);
        session.Window.MouseMove(to);
        session.Window.MouseUp(to, MouseButton.Left);
        session.Pump();
        session.Screenshot("body-selection");

        Assert.Equal(new Rect(340, 150, 600, 500), overlay.SelectionBounds);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "latest.json")));
        Assert.True(document.RootElement.GetProperty("active").GetBoolean());
        Assert.Equal(600, document.RootElement.GetProperty("bounds").GetProperty("width").GetDouble());
        Assert.Equal(500, document.RootElement.GetProperty("bounds").GetProperty("height").GetDouble());

        mainWindow.OnRegionReviewDeactivated(null, EventArgs.Empty);
        Assert.True(overlay.IsReviewActive);
        using (var preserved = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "latest.json"))))
            Assert.True(preserved.RootElement.GetProperty("active").GetBoolean());

        mainWindow.Width = 1000;
        session.Pump();
        Assert.False(overlay.IsReviewActive);
        using var cleared = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "latest.json")));
        Assert.True(cleared.RootElement.GetProperty("active").GetBoolean());
        Assert.True(cleared.RootElement.GetProperty("suspended").GetBoolean());

        mainWindow.StartRegionReview(outputDirectory);
        session.Pump();
        Assert.Equal(default, overlay.SelectionBounds);
        Drag(session, new Point(60, 180), new Point(220, 320));
        Assert.Equal(2, overlay.SelectedFigureNumber);
        var resizedPath = session.Screenshot("new-size-live-layout");
        using (var resizedRecord = ReadRecord(outputDirectory))
        {
            var captures = resizedRecord.RootElement.GetProperty("captures").EnumerateArray().ToArray();
            Assert.Equal(2, captures.Length);
            Assert.NotEqual(captures[0].GetProperty("logicalWidth").GetDouble(),
                captures[1].GetProperty("logicalWidth").GetDouble());
            Assert.NotEqual(captures[0].GetProperty("imagePath").GetString(),
                captures[1].GetProperty("imagePath").GetString());
        }
        Assert.True(File.Exists(resizedPath));
        session.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        mainWindow.Width = originalWidth;
        session.Pump();
        mainWindow.StartRegionReview(outputDirectory);
        session.Pump();
        Assert.Equal(new Rect(340, 150, 600, 500), overlay.SelectionBounds);
        var restoredPath = session.Screenshot("restored-original-size");
        using var restored = SkiaSharp.SKBitmap.Decode(restoredPath);
        Assert.True(restored.GetPixel(350, 150).Red > 200);
        var restoredPencil = session.Find<Button>(button => button.Name == "EditFigure1Note");
        session.Click(restoredPencil);
        session.Pump();
        Assert.True(overlay.IsEditingNote);
        Assert.Equal(2, overlay.FigureCount);
        session.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        session.Pump();
        session.Window.MouseDown(new Point(338, 138), MouseButton.Left);
        session.Window.MouseUp(new Point(338, 138), MouseButton.Left);
        session.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        session.Pump();
        Assert.Equal(1, overlay.FigureCount);
        var deletedPath = session.Screenshot("restored-after-delete");
        using var deleted = SkiaSharp.SKBitmap.Decode(deletedPath);
        Assert.True(deleted.GetPixel(600, 150).Red < 200);
    }

    [AvaloniaFact]
    [Trait("Category", "Diagnostic")]
    public void MainWindow_PreservesFiguresAcrossHomeAndSettingsCaptures()
    {
        using var session = UiSession.ForMainWindow("region-review-pages");
        var mainWindow = Assert.IsType<Views.MainWindow>(session.Window);
        var viewModel = Assert.IsType<ViewModels.MainWindowViewModel>(mainWindow.DataContext);
        var outputDirectory = Path.Combine(session.ShotDirectory, "records");

        mainWindow.StartRegionReview(outputDirectory);
        session.Pump();
        Drag(session, new Point(120, 140), new Point(280, 260));
        session.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        session.Pump();

        viewModel.OpenSettingsCommand.Execute(null);
        session.Pump();
        mainWindow.StartRegionReview(outputDirectory);
        session.Pump();
        // Reuse the first page coordinates. An archived badge must not intercept this drag.
        Drag(session, new Point(120, 140), new Point(280, 260));
        session.Screenshot("settings-figure-two");

        using (var document = ReadRecord(outputDirectory))
        {
            var figures = document.RootElement.GetProperty("figures").EnumerateArray().ToArray();
            Assert.Equal([1, 2], figures.Select(item => item.GetProperty("number").GetInt32()).ToArray());
            Assert.StartsWith("/home", figures[0].GetProperty("pageRoute").GetString());
            Assert.StartsWith("/settings", figures[1].GetProperty("pageRoute").GetString());
            Assert.NotEqual(figures[0].GetProperty("captureId").GetString(), figures[1].GetProperty("captureId").GetString());
            Assert.All(figures, figure => Assert.True(File.Exists(figure.GetProperty("imagePath").GetString())));
            Assert.Equal(2, document.RootElement.GetProperty("captures").GetArrayLength());
        }

        session.Window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
        session.Pump();
        var editor = session.Find<TextBox>(box => box.Watermark as string == "Optional note for this figure");
        session.Type(editor, "Settings note");
        session.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control | RawInputModifiers.Shift);
        session.Pump();
        using (var suspendedNote = ReadRecord(outputDirectory))
        {
            Assert.True(suspendedNote.RootElement.GetProperty("suspended").GetBoolean());
            Assert.Equal("Settings note", suspendedNote.RootElement.GetProperty("figures")[1].GetProperty("note").GetString());
        }

        mainWindow.StartRegionReview(outputDirectory);
        session.Pump();
        session.Window.MouseDown(new Point(40, 80), MouseButton.Left);
        session.Window.MouseMove(new Point(700, 500));
        session.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        session.Pump();
        using (var canceledDrag = ReadRecord(outputDirectory))
            Assert.Equal(2, canceledDrag.RootElement.GetProperty("figures").GetArrayLength());

        mainWindow.StartRegionReview(outputDirectory);
        session.Pump();
        Drag(session, new Point(620, 180), new Point(760, 300));
        session.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        session.Pump();
        using (var retainedPages = ReadRecord(outputDirectory))
        {
            Assert.Equal(2, retainedPages.RootElement.GetProperty("figures").GetArrayLength());
            Assert.StartsWith("/settings",
                retainedPages.RootElement.GetProperty("figures")[1].GetProperty("pageRoute").GetString());
            Assert.Equal(retainedPages.RootElement.GetProperty("figures")[1].GetProperty("imagePath").GetString(),
                retainedPages.RootElement.GetProperty("imagePath").GetString());
            Assert.Equal(2, retainedPages.RootElement.GetProperty("captures").GetArrayLength());
        }

        session.Window.KeyPressQwerty(PhysicalKey.A,
            RawInputModifiers.Control | RawInputModifiers.Shift | RawInputModifiers.Alt);
        session.Pump();
        using var reset = ReadRecord(outputDirectory);
        Assert.False(reset.RootElement.GetProperty("active").GetBoolean());
        Assert.Empty(reset.RootElement.GetProperty("figures").EnumerateArray());
    }

    [AvaloniaFact]
    public void Drag_CapturesNormalizedRegionWithoutActivatingUnderlyingButton()
    {
        var clickCount = 0;
        var button = new Button
        {
            Content = "Original content",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };
        button.Click += (_, _) => clickCount++;
        var root = new Grid();
        root.Children.Add(button);

        using var session = UiSession.ForView(root, new object(), "region-review", 640, 420);
        var outputDirectory = Path.Combine(session.ShotDirectory, "records");
        var overlay = new RegionReviewOverlay(session.Window, outputDirectory);
        overlay.SetValue(Panel.ZIndexProperty, int.MaxValue);
        root.Children.Add(overlay);
        session.Pump();

        overlay.Start();
        session.Pump();
        var frozenBefore = session.Screenshot("frozen-before-content-change");
        button.Content = "Changed after capture";
        session.Pump();
        var frozenAfter = session.Screenshot("frozen-after-content-change");
        Assert.Equal(Hash(frozenBefore), Hash(frozenAfter));

        var from = new Point(510, 330);
        var to = new Point(140, 90);
        session.Window.MouseMove(from);
        session.Window.MouseDown(from, MouseButton.Left);
        session.Window.MouseMove(to);
        session.Window.MouseUp(to, MouseButton.Left);
        session.Pump();
        session.Screenshot("reverse-drag-selection");

        Assert.Equal(0, clickCount);
        Assert.Equal(new Rect(140, 90, 370, 240), overlay.SelectionBounds);

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "latest.json")));
        var rootElement = document.RootElement;
        Assert.Equal(3, rootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(rootElement.GetProperty("active").GetBoolean());
        Assert.NotEmpty(rootElement.GetProperty("sessionId").GetString()!);
        var selectionId = rootElement.GetProperty("selectionId").GetString()!;
        Assert.NotEmpty(selectionId);
        Assert.Equal(140, rootElement.GetProperty("bounds").GetProperty("x").GetDouble());
        Assert.Equal(90, rootElement.GetProperty("bounds").GetProperty("y").GetDouble());
        Assert.Equal(370, rootElement.GetProperty("bounds").GetProperty("width").GetDouble());
        Assert.Equal(240, rootElement.GetProperty("bounds").GetProperty("height").GetDouble());
        Assert.True(rootElement.GetProperty("processId").GetInt32() > 0);
        var annotatedImagePath = rootElement.GetProperty("imagePath").GetString()!;
        Assert.True(File.Exists(annotatedImagePath));
        Assert.NotEqual(Hash(frozenBefore), Hash(annotatedImagePath));

        session.Window.MouseDown(new Point(50, 50), MouseButton.Left);
        session.Window.MouseMove(new Point(80, 80));
        overlay.CancelDrag();
        session.Pump();
        Assert.Equal(new Rect(140, 90, 370, 240), overlay.SelectionBounds);
        using var restored = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "latest.json")));
        Assert.True(restored.RootElement.GetProperty("active").GetBoolean());
        Assert.Equal(selectionId, restored.RootElement.GetProperty("selectionId").GetString());
    }

    [AvaloniaFact]
    [Trait("Category", "Diagnostic")]
    public void MainWindow_AddsNotesSelectsAndDeletesStableNumberedFigures()
    {
        using var session = UiSession.ForMainWindow("region-review-multiple");
        var mainWindow = Assert.IsType<Views.MainWindow>(session.Window);
        var outputDirectory = Path.Combine(session.ShotDirectory, "records");
        mainWindow.StartRegionReview(outputDirectory);
        session.Pump();

        Drag(session, new Point(100, 100), new Point(250, 220));
        Drag(session, new Point(300, 180), new Point(500, 330));
        var overlay = Assert.IsType<RegionReviewOverlay>(mainWindow.RegionReviewOverlay);
        Assert.Equal(2, overlay.FigureCount);
        Assert.Equal(2, overlay.SelectedFigureNumber);

        var pencil = session.Find<Button>(button => button.Name == "EditFigure2Note");
        session.Screenshot("pencil-affordances");
        session.Click(pencil);
        session.Pump();
        Assert.True(overlay.IsEditingNote);
        Assert.Equal(2, overlay.SelectedFigureNumber);
        Assert.Equal(2, overlay.FigureCount);
        var editor = session.Find<TextBox>(box => box.Watermark as string == "Optional note for this figure");
        Assert.True(editor.IsFocused);
        var letterKey = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.N,
            Source = editor,
        };
        editor.RaiseEvent(letterKey);
        Assert.False(letterKey.Handled);
        if (!letterKey.Handled)
        {
            editor.RaiseEvent(new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Text = "n",
                Source = editor,
            });
        }
        Assert.Equal("n", editor.Text);
        editor.SelectAll();
        var deleteKey = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Delete,
            Source = editor,
        };
        editor.RaiseEvent(deleteKey);
        Assert.Equal(string.Empty, editor.Text);
        Assert.Equal(2, overlay.FigureCount);
        var cancelButton = session.Find<Button>(button => button.IsVisible && Equals(button.Content, "Cancel"));
        var saveButton = session.Find<Button>(button => button.IsVisible && Equals(button.Content, "Save"));
        session.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        session.Pump();
        Assert.True(cancelButton.IsFocused);
        session.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        session.Pump();
        Assert.True(saveButton.IsFocused);
        session.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        session.Pump();
        Assert.True(editor.IsFocused);
        session.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
        session.Pump();
        Assert.True(saveButton.IsFocused);
        editor.Focus();
        session.Screenshot("note-editor");
        session.Type(editor, "Tighten this spacing");
        session.ClickText("Save");
        Assert.False(overlay.IsEditingNote);

        session.Click(pencil);
        session.Pump();
        session.Type(editor, " but cancel this");
        session.Window.MouseMove(new Point(88, 88));
        session.Window.MouseDown(new Point(88, 88), MouseButton.Left);
        session.Window.MouseUp(new Point(88, 88), MouseButton.Left);
        session.Pump();
        Assert.Equal(2, overlay.SelectedFigureNumber);
        Assert.True(overlay.IsEditingNote);
        session.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        session.Pump();
        Assert.False(overlay.IsEditingNote);
        Assert.True(overlay.IsReviewActive);

        using (var noted = ReadRecord(outputDirectory))
        {
            Assert.Equal(new[] { 1, 2 }, noted.RootElement.GetProperty("figures").EnumerateArray()
                .Select(figure => figure.GetProperty("number").GetInt32()).ToArray());
            Assert.Equal("Tighten this spacing", noted.RootElement.GetProperty("figures")[1].GetProperty("note").GetString());
            using var image = SkiaSharp.SKBitmap.Decode(noted.RootElement.GetProperty("imagePath").GetString());
            Assert.True(image.GetPixel(100, 100).Red > 200);
            Assert.True(image.GetPixel(300, 180).Red > 200);
        }

        session.Window.MouseMove(new Point(88, 88));
        session.Window.MouseDown(new Point(88, 88), MouseButton.Left);
        session.Window.MouseUp(new Point(88, 88), MouseButton.Left);
        session.Pump();
        Assert.Equal(1, overlay.SelectedFigureNumber);
        session.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        session.Pump();
        using (var afterDelete = ReadRecord(outputDirectory))
        {
            var remaining = Assert.Single(afterDelete.RootElement.GetProperty("figures").EnumerateArray());
            Assert.Equal(2, remaining.GetProperty("number").GetInt32());
        }

        Drag(session, new Point(560, 350), new Point(760, 520));
        using (var afterAdd = ReadRecord(outputDirectory))
        {
            Assert.Equal(new[] { 2, 3 }, afterAdd.RootElement.GetProperty("figures").EnumerateArray()
                .Select(figure => figure.GetProperty("number").GetInt32()).ToArray());
            session.Screenshot("figures-two-and-three");
        }

        session.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        session.Pump();
        session.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        session.Pump();
        using var inactive = ReadRecord(outputDirectory);
        Assert.False(inactive.RootElement.GetProperty("active").GetBoolean());
        Assert.Empty(inactive.RootElement.GetProperty("figures").EnumerateArray());

        var oldSession = inactive.RootElement.GetProperty("sessionId").GetString();
        overlay.Start();
        session.Pump();
        using var nextSession = ReadRecord(outputDirectory);
        Assert.Equal(oldSession, nextSession.RootElement.GetProperty("sessionId").GetString());
    }

    [AvaloniaFact]
    public void Escape_ClearsSelectionAndWritesInactiveRecord()
    {
        var root = new Grid();
        using var session = UiSession.ForView(root, new object(), "region-review-clear", 500, 300);
        var outputDirectory = Path.Combine(session.ShotDirectory, "records");
        var overlay = new RegionReviewOverlay(session.Window, outputDirectory);
        overlay.SetValue(Panel.ZIndexProperty, int.MaxValue);
        root.Children.Add(overlay);
        session.Pump();
        overlay.Start();
        session.Pump();

        session.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        session.Pump();

        Assert.False(overlay.IsReviewActive);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "latest.json")));
        Assert.False(document.RootElement.GetProperty("active").GetBoolean());
        Assert.Equal(string.Empty, document.RootElement.GetProperty("imagePath").GetString());
    }

    [AvaloniaFact]
    public void FailedInvalidationAndCapture_DoNotPublishOrAcceptAStaleSelection()
    {
        var root = new Grid();
        using var session = UiSession.ForView(root, new object(), "region-review-failures", 500, 300);
        var outputDirectory = Path.Combine(session.ShotDirectory, "records");
        var store = new FaultingRegionReviewStore(outputDirectory);
        var overlay = new RegionReviewOverlay(session.Window, store);
        overlay.SetValue(Panel.ZIndexProperty, int.MaxValue);
        root.Children.Add(overlay);
        session.Pump();
        overlay.Start();
        session.Pump();

        session.Window.MouseDown(new Point(50, 60), MouseButton.Left);
        session.Window.MouseMove(new Point(250, 180));
        session.Window.MouseUp(new Point(250, 180), MouseButton.Left);
        session.Pump();
        var originalBounds = overlay.SelectionBounds;
        using var original = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "latest.json")));
        var originalId = original.RootElement.GetProperty("selectionId").GetString();

        store.FailWrites = true;
        session.Window.MouseDown(new Point(10, 10), MouseButton.Left);
        session.Window.MouseMove(new Point(400, 250));
        session.Window.MouseUp(new Point(400, 250), MouseButton.Left);
        session.Pump();

        Assert.Equal(originalBounds, overlay.SelectionBounds);
        Assert.True(overlay.IsReviewActive);
        using var preserved = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "latest.json")));
        Assert.Equal(originalId, preserved.RootElement.GetProperty("selectionId").GetString());

        store.FailWrites = false;
        overlay.Clear();
        overlay.CaptureOverride = () => throw new InvalidOperationException("Injected capture failure.");
        overlay.Start();
        session.Pump();
        Assert.False(overlay.CanSelect);
        session.Window.MouseDown(new Point(20, 20), MouseButton.Left);
        session.Window.MouseMove(new Point(300, 220));
        session.Window.MouseUp(new Point(300, 220), MouseButton.Left);
        session.Pump();
        Assert.Equal(default, overlay.SelectionBounds);
        using var inactive = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "latest.json")));
        Assert.False(inactive.RootElement.GetProperty("active").GetBoolean());
    }

    [AvaloniaFact]
    public void HighDpiFrame_PreservesFarEdgeContentAndSelectionCoordinates()
    {
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        {
            var canvas = new Canvas { Background = Avalonia.Media.Brushes.White };
            var landmark = new Border
            {
                Width = 50, Height = 50, Background = Avalonia.Media.Brushes.Lime,
            };
            Canvas.SetLeft(landmark, 570);
            Canvas.SetTop(landmark, 330);
            canvas.Children.Add(landmark);
            var root = new Grid();
            root.Children.Add(canvas);
            using var session = UiSession.ForView(root, new object(), $"region-review-dpi-{scale * 100:0}", 640, 420);
            var outputDirectory = Path.Combine(session.ShotDirectory, "records");
            var overlay = new RegionReviewOverlay(session.Window, outputDirectory);
            root.Children.Add(overlay);
            session.Pump();
            overlay.CaptureOverride = () =>
            {
                var frame = new Avalonia.Media.Imaging.RenderTargetBitmap(
                    new PixelSize((int)(640 * scale), (int)(420 * scale)), new Vector(96 * scale, 96 * scale));
                frame.Render(session.Window);
                return frame;
            };
            overlay.Start();
            session.Pump();
            session.Window.MouseDown(new Point(550, 310), MouseButton.Left);
            session.Window.MouseMove(new Point(630, 395));
            session.Window.MouseUp(new Point(630, 395), MouseButton.Left);
            session.Pump();
            var screenshot = session.Screenshot("far-edge-selection");
            using var displayed = SkiaSharp.SKBitmap.Decode(screenshot);
            Assert.Equal(SkiaSharp.SKColors.Lime, displayed.GetPixel(595, 355));
            Assert.Equal(new Rect(550, 310, 80, 85), overlay.SelectionBounds);
            using var record = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "latest.json")));
            using var saved = SkiaSharp.SKBitmap.Decode(record.RootElement.GetProperty("imagePath").GetString());
            Assert.Equal(SkiaSharp.SKColors.Lime, saved.GetPixel(595, 355));
            Assert.Equal((int)Math.Ceiling(640 * session.Window.RenderScaling), saved.Width);
            Assert.Equal(550, record.RootElement.GetProperty("bounds").GetProperty("x").GetDouble());
            overlay.Clear();
        }
    }
    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Drag(UiSession session, Point from, Point to)
    {
        session.Window.MouseMove(from);
        session.Window.MouseDown(from, MouseButton.Left);
        session.Window.MouseMove(to);
        session.Window.MouseUp(to, MouseButton.Left);
        session.Pump();
    }

    private static JsonDocument ReadRecord(string outputDirectory) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "latest.json")));
}
#endif
