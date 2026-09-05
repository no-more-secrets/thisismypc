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
    [Trait("Category", "Diagnostic")]
    public void MainWindow_OverlaySpansBodyAndCapturesBodyDrag()
    {
        using var session = UiSession.ForMainWindow("region-review-main-window");
        var mainWindow = Assert.IsType<Views.MainWindow>(session.Window);
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

        mainWindow.Width += 20;
        session.Pump();
        Assert.False(overlay.IsReviewActive);
        using var cleared = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "latest.json")));
        Assert.False(cleared.RootElement.GetProperty("active").GetBoolean());
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
        Assert.Equal(1, rootElement.GetProperty("schemaVersion").GetInt32());
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

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
#endif
