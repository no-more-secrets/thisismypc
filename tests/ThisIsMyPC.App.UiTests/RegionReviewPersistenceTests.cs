#if DEBUG
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using ThisIsMyPC.App.Diagnostics;

namespace ThisIsMyPC.App.UiTests;

public sealed class RegionReviewPersistenceTests
{
    private sealed class FailingStore(string directory) : RegionReviewStore(directory)
    {
        public bool Fail { get; set; }
        internal override void Write(RegionReviewRecord record)
        {
            if (Fail) throw new IOException("Injected failure");
            base.Write(record);
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EmptyOrMissingHistoryRestoresWithoutLosingOpenNotes(bool omitHistory, bool hasNote)
    {
        var root = new Grid { Background = Brushes.White };
        using var session = UiSession.ForView(root, new object(), "region-review-null-history", 640, 420);
        var directory = Path.Combine(session.ShotDirectory, Guid.NewGuid().ToString("N"));
        var overlay = new RegionReviewOverlay(session.Window, directory);
        root.Children.Add(overlay);
        overlay.Start(); session.Pump();
        if (hasNote)
        {
            Drag(session, 50, 80, 250, 200);
            overlay.EditSelectedNote(); session.Pump();
            session.Find<TextBox>(_ => true).Text = "Preserve this open note";
        }
        overlay.Close(); root.Children.Remove(overlay);
        var path = Path.Combine(directory, "latest.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        if (omitHistory) json.AsObject().Remove("resolvedFigures");
        else json["resolvedFigures"] = null;
        File.WriteAllText(path, json.ToJsonString());

        overlay = new RegionReviewOverlay(session.Window, directory);
        root.Children.Add(overlay);
        overlay.Start(); session.Pump();
        Assert.True(overlay.CanSelect);
        Assert.Equal(hasNote ? 1 : 0, overlay.FigureCount);
        Assert.Equal(0, overlay.ResolvedFigureCount);
        if (hasNote)
        {
            overlay.EditSelectedNote(); session.Pump();
            Assert.Equal("Preserve this open note", session.Find<TextBox>(_ => true).Text);
            overlay.CancelNote();
        }
        session.Screenshot($"restored-{omitHistory}-{hasNote}");
        Drag(session, 350, 80, 500, 200);
        Assert.Equal(hasNote ? 2 : 1, overlay.SelectedFigureNumber);
        overlay.Close();
        Assert.Empty(Read(directory).ResolvedFigures);
        Assert.Equal(hasNote ? 2 : 1, Read(directory).Figures.Count);
    }

    [AvaloniaFact]
    public void RestartPreservesNotesHistoryRoutesDimensionsAndNumbers()
    {
        var root = new Grid { Background = Brushes.White };
        using var session = UiSession.ForView(root, new object(), "region-review-persistence", 900, 650);
        var directory = Path.Combine(session.ShotDirectory, Guid.NewGuid().ToString("N"));
        var route = "/home";
        var overlay = new RegionReviewOverlay(session.Window, directory, () => route);
        root.Children.Add(overlay);
        overlay.Start(); session.Pump();
        Drag(session, 50, 80, 250, 200);
        overlay.EditSelectedNote(); session.Pump();
        session.Find<TextBox>(t => t.IsVisible).Text = "Keep this feedback after restart";
        overlay.Suspend();
        route = "/settings";
        session.Window.Width = 1000; session.Pump();
        overlay.Start(); session.Pump();
        Drag(session, 80, 100, 300, 240);
        var initial = Read(directory);
        var first = initial.Figures[0];
        var second = initial.Figures[1];
        Assert.True(overlay.SetResolved(first.Id, true, "Verified patch"));
        overlay.Close(); overlay.Close(); root.Children.Remove(overlay);
        var closed = Read(directory);
        Assert.Equal("Keep this feedback after restart", Assert.Single(closed.ResolvedFigures).Note);
        Assert.Equal(3, closed.NextFigureNumber);
        Assert.Single(closed.Figures);
        overlay = new RegionReviewOverlay(session.Window, directory, () => route);
        root.Children.Add(overlay);
        overlay.Start(); session.Pump();
        Assert.Equal(initial.SessionId, Read(directory).SessionId);
        Assert.Equal(new Rect(80, 100, 220, 140), overlay.SelectionBounds);
        Assert.Equal(1, overlay.ResolvedFigureCount);
        overlay.ShowNotes(); session.Pump();
        session.Click(session.Find<CheckBox>(c => Equals(c.Content, "Show resolved notes")));
        session.Pump();
        session.Screenshot("history-dark");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("history-light");
        session.Click(session.Find<Button>(b => b.Name == "StatusFigure1"));
        Assert.Equal(2, overlay.FigureCount);
        overlay.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(overlay.IsNotesOpen);
        Assert.True(overlay.IsReviewActive);
        overlay.Suspend();
        route = "/home";
        session.Window.Width = 900; session.Pump();
        overlay.Start(); session.Pump();
        Assert.Equal(new Rect(50, 80, 200, 120), overlay.SelectionBounds);
        overlay.EditSelectedNote(); session.Pump();
        Assert.Equal("Keep this feedback after restart", session.Find<TextBox>(t => t.IsVisible).Text);
        overlay.CancelNote();
        overlay.DeleteSelectedFigure();
        Assert.True(overlay.SetResolved(second.Id, true));
        overlay.Close(); root.Children.Remove(overlay);
        overlay = new RegionReviewOverlay(session.Window, directory, () => route);
        root.Children.Add(overlay); overlay.Start(); session.Pump();
        Drag(session, 40, 80, 150, 160);
        Assert.Equal(3, overlay.SelectedFigureNumber);
        Assert.Single(Read(directory).ResolvedFigures);
        overlay.Close();
    }

    [AvaloniaFact]
    public void ResolveReopenUsesCleanFrameAndKeepsHistoryWhenClearing()
    {
        var root = new Grid { Background = Brushes.White };
        using var session = UiSession.ForView(root, new object(), "region-review-clean-frame", 640, 420);
        var directory = Path.Combine(session.ShotDirectory, Guid.NewGuid().ToString("N"));
        var overlay = new RegionReviewOverlay(session.Window, directory);
        root.Children.Add(overlay); overlay.Start(); session.Pump();
        Drag(session, 50, 80, 250, 200);
        Drag(session, 350, 100, 500, 250);
        var first = Read(directory).Figures[0];
        Assert.True(overlay.SetResolved(first.Id, true));
        overlay.Close(); root.Children.Remove(overlay);
        overlay = new RegionReviewOverlay(session.Window, directory);
        root.Children.Add(overlay); overlay.Start(); session.Pump();
        using var pixels = SkiaSharp.SKBitmap.Decode(session.Screenshot("restored-without-ghost"));
        Assert.Equal(SkiaSharp.SKColors.White, pixels.GetPixel(50, 150));
        Assert.Equal(1, overlay.FigureCount);
        Assert.True(overlay.SetResolved(first.Id, false));
        Assert.Equal(2, overlay.FigureCount);
        Assert.True(overlay.SetResolved(first.Id, true));
        overlay.Clear();
        Assert.Single(Read(directory).ResolvedFigures);
        Assert.Empty(Read(directory).Figures);
        Assert.Equal(3, Read(directory).NextFigureNumber);
        overlay.Close();
    }

    [AvaloniaFact]
    public void FailedWritesAndSecondInstanceCannotRemoveSavedNotes()
    {
        var root = new Grid();
        using var session = UiSession.ForView(root, new object(), "region-review-failure", 640, 420);
        var directory = Path.Combine(session.ShotDirectory, Guid.NewGuid().ToString("N"));
        var store = new FailingStore(directory);
        var overlay = new RegionReviewOverlay(session.Window, store);
        root.Children.Add(overlay); overlay.Start(); session.Pump();
        Drag(session, 50, 80, 250, 200);
        var first = Read(directory).Figures[0];
        var before = File.ReadAllText(Path.Combine(directory, "latest.json"));
        store.Fail = true;
        Assert.False(overlay.SetResolved(first.Id, true));
        Assert.Equal(1, overlay.FigureCount);
        overlay.DeleteSelectedFigure();
        Assert.Equal(1, overlay.FigureCount);
        Assert.Equal(before, File.ReadAllText(Path.Combine(directory, "latest.json")));
        var other = new RegionReviewOverlay(session.Window, directory);
        root.Children.Add(other); other.Start(); other.Clear(); other.Close();
        Assert.Equal(before, File.ReadAllText(Path.Combine(directory, "latest.json")));
        overlay.Close();
        Assert.Equal(before, File.ReadAllText(Path.Combine(directory, "latest.json")));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CorruptRecordIsNotReplacedAndMissingFrameKeepsFeedbackReadable(bool corruptFrame)
    {
        var root = new Grid();
        using var session = UiSession.ForView(root, new object(), "region-review-recovery", 640, 420);
        var directory = Path.Combine(session.ShotDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "latest.json");
        File.WriteAllText(path, "{broken");
        var overlay = new RegionReviewOverlay(session.Window, directory);
        root.Children.Add(overlay); overlay.Start(); overlay.Clear(); overlay.Close(); root.Children.Remove(overlay);
        Assert.Equal("{broken", File.ReadAllText(path));
        File.Delete(path);
        overlay = new RegionReviewOverlay(session.Window, directory);
        root.Children.Add(overlay); overlay.Start(); session.Pump();
        Drag(session, 50, 80, 250, 200);
        var record = Read(directory);
        overlay.Close(); root.Children.Remove(overlay);
        var rawPath = Assert.Single(record.Captures).RawImagePath!;
        if (corruptFrame) File.WriteAllText(rawPath, "not a PNG"); else File.Delete(rawPath);
        overlay = new RegionReviewOverlay(session.Window, directory);
        root.Children.Add(overlay); overlay.Start(); overlay.ShowNotes(); session.Pump();
        Assert.Equal(1, overlay.FigureCount);
        if (corruptFrame) Assert.False(overlay.CanSelect);
        else Assert.NotNull(session.Find<TextBlock>(t => t.Text?.StartsWith("Reference capture only", StringComparison.Ordinal) == true));
        session.Click(session.Find<Button>(b => b.Name == "StatusFigure1"));
        Assert.Single(Read(directory).ResolvedFigures);
        overlay.Close();
    }

    [AvaloniaFact]
    public void AgentCommandsDeferDuringEditingAndValidateSession()
    {
        var root = new Grid();
        using var session = UiSession.ForView(root, new object(), "region-review-commands", 640, 420);
        var directory = Path.Combine(session.ShotDirectory, Guid.NewGuid().ToString("N"));
        var overlay = new RegionReviewOverlay(session.Window, directory);
        root.Children.Add(overlay); overlay.Start(); session.Pump();
        Drag(session, 50, 80, 250, 200);
        var record = Read(directory);
        var commandDirectory = Path.Combine(directory, "commands");
        Directory.CreateDirectory(commandDirectory);
        string Queue(string sessionId)
        {
            var id = Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(commandDirectory, id + ".json"), JsonSerializer.Serialize(new RegionReviewCommand
            { Id = id, SessionId = sessionId, FigureId = record.Figures[0].Id, Status = "resolved", ResolutionNote = "Tests passed" },
                RegionReviewJsonContext.Default.RegionReviewCommand));
            return Path.Combine(commandDirectory, id + ".result");
        }
        var wrong = Queue("other-session"); overlay.ProcessCommands();
        Assert.False(JsonDocument.Parse(File.ReadAllText(wrong)).RootElement.GetProperty("applied").GetBoolean());
        overlay.EditSelectedNote();
        var correct = Queue(record.SessionId); overlay.ProcessCommands();
        Assert.False(File.Exists(correct));
        overlay.CancelNote(); overlay.ProcessCommands();
        Assert.True(JsonDocument.Parse(File.ReadAllText(correct)).RootElement.GetProperty("applied").GetBoolean());
        Assert.Empty(Read(directory).Figures);
        Assert.Single(Read(directory).ResolvedFigures);
        overlay.Close();
    }

    private static RegionReviewRecord Read(string directory) => JsonSerializer.Deserialize(
        File.ReadAllText(Path.Combine(directory, "latest.json")), RegionReviewJsonContext.Default.RegionReviewRecord)!;
    private static void Drag(UiSession session, double x1, double y1, double x2, double y2)
    {
        session.Window.MouseMove(new Point(x1, y1));
        session.Window.MouseDown(new Point(x1, y1), MouseButton.Left);
        session.Window.MouseMove(new Point(x2, y2));
        session.Window.MouseUp(new Point(x2, y2), MouseButton.Left);
        session.Pump();
    }
}
#endif
