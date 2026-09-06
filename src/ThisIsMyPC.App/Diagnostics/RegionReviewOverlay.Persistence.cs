#if DEBUG
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace ThisIsMyPC.App.Diagnostics;

internal sealed partial class RegionReviewOverlay
{
    private void LoadSavedReview()
    {
        store.Acquire();
        var record = store.Read();
        if (record is not null)
        {
            foreach (var capture in record.Captures)
                captures.Add(new CaptureState(capture.Id, capture.PageRoute, capture.CapturedAtUtc,
                    capture.ImagePath, capture.RenderScale, capture.PixelWidth, capture.PixelHeight,
                    capture.LogicalWidth, capture.LogicalHeight, null)
                { LayoutState = capture.LayoutState, RawImagePath = capture.RawImagePath });
            FigureState Restore(RegionReviewFigure figure) => new(figure.Number, figure.Id,
                new Rect(figure.Bounds.X, figure.Bounds.Y, figure.Bounds.Width, figure.Bounds.Height),
                figure.Note, figure.CaptureId, figure.PageRoute, figure.CapturedAtUtc, figure.ImagePath)
            { ResolvedAtUtc = figure.ResolvedAtUtc, ResolutionNote = figure.ResolutionNote };
            figures.AddRange(record.Figures.Select(Restore));
            resolvedFigures.AddRange(record.ResolvedFigures.Select(Restore));
            nextFigureNumber = Math.Max(record.NextFigureNumber,
                figures.Concat(resolvedFigures).Select(f => f.Number).DefaultIfEmpty(0).Max() + 1);
            foreach (var figure in figures) AddPencilButton(figure);
            activeRecord = record;
        }
        loaded = true;
        commandTimer.Start();
    }

    internal bool SetResolved(string figureId, bool resolved, string? resolutionNote = null)
    {
        var source = resolved ? figures : resolvedFigures;
        var target = resolved ? resolvedFigures : figures;
        var figure = source.FirstOrDefault(f => f.Id == figureId);
        if (figure is null) return target.Any(f => f.Id == figureId);
        var index = source.IndexOf(figure);
        var previousTime = figure.ResolvedAtUtc;
        var previousNote = figure.ResolutionNote;
        var previousSelection = selectedFigureNumber;
        source.Remove(figure);
        target.Add(figure);
        figure.ResolvedAtUtc = resolved ? DateTime.UtcNow : null;
        figure.ResolutionNote = resolved ? resolutionNote : null;
        if (resolved) RemovePencilButton(figure.Number); else AddPencilButton(figure);
        selectedFigureNumber = CurrentFigures.LastOrDefault()?.Number;
        var saved = currentCapture?.Frame is not null && figure.CaptureId == currentCapture.Id && !string.IsNullOrEmpty(currentCapture.RawImagePath)
            ? SaveCurrentState(suspended: !IsVisible) : WriteExistingRecord(suspended: !IsVisible);
        if (!saved)
        {
            target.Remove(figure);
            source.Insert(index, figure);
            figure.ResolvedAtUtc = previousTime;
            figure.ResolutionNote = previousNote;
            selectedFigureNumber = previousSelection;
            if (resolved) AddPencilButton(figure); else RemovePencilButton(figure.Number);
            RestoreActiveRecord();
        }
        if (IsNotesOpen) RefreshNotes();
        InvalidateArrange();
        InvalidateVisual();
        return saved;
    }

    internal void ShowNotes()
    {
        if (IsEditingNote) { SaveNote(); if (IsEditingNote) return; }
        notesHost.IsVisible = true;
        RefreshNotes();
        InvalidateArrange();
    }

    internal void HideNotes()
    {
        notesHost.IsVisible = false;
        notesList.Children.Clear();
        foreach (var image in previews) image.Dispose();
        previews.Clear();
        InvalidateArrange();
    }

    private bool IsFromNotes(object? source) => source is Visual visual
        && (ReferenceEquals(visual, notesButton) || visual.GetVisualAncestors().Contains(notesButton)
            || ReferenceEquals(visual, notesHost) || visual.GetVisualAncestors().Contains(notesHost));

    private void RefreshNotes()
    {
        notesList.Children.Clear();
        foreach (var image in previews) image.Dispose();
        previews.Clear();
        var visible = (showResolved.IsChecked == true ? figures.Concat(resolvedFigures) : figures)
            .OrderBy(f => f.Number).ToArray();
        if (visible.Length == 0)
            notesList.Children.Add(new TextBlock { Text = "No open notes. Enable resolved notes to view history.", TextWrapping = TextWrapping.Wrap });
        foreach (var figure in visible)
        {
            var capture = captures.First(c => c.Id == figure.CaptureId);
            var isResolved = figure.ResolvedAtUtc is not null;
            var rows = new StackPanel { Spacing = 6 };
            rows.Children.Add(new TextBlock { Text = $"Fig. {figure.Number}: {(isResolved ? "Resolved" : "Open")}", FontWeight = FontWeight.SemiBold });
            rows.Children.Add(new TextBlock { Text = $"{figure.PageRoute} | {capture.LogicalWidth:0} x {capture.LogicalHeight:0} | {capture.RenderScale:0.##}x",
                TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            rows.Children.Add(new TextBlock { Text = figure.Note ?? "No text note", TextWrapping = TextWrapping.Wrap });
            if (isResolved)
                rows.Children.Add(new TextBlock { Text = $"{figure.ResolvedAtUtc:yyyy-MM-dd HH:mm} UTC. {figure.ResolutionNote}", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            var change = new Button { Content = isResolved ? "Reopen" : "Resolve", Name = $"StatusFigure{figure.Number}" };
            change.Click += (_, _) => SetResolved(figure.Id, !isResolved, "Resolved in app");
            var preview = new Image { MaxHeight = 220, Stretch = Stretch.Uniform, IsVisible = false };
            var view = new Button { Content = "View capture" };
            view.Click += (_, _) =>
            {
                try
                {
                    if (preview.Source is null)
                    {
                        var bitmap = new Bitmap(store.ValidateImagePath(figure.ImagePath));
                        previews.Add(bitmap);
                        preview.Source = bitmap;
                    }
                    preview.IsVisible = !preview.IsVisible;
                }
                catch (Exception exception) { failureMessage = $"Saved capture unavailable: {exception.Message}"; InvalidateVisual(); }
            };
            actions.Children.Add(change);
            actions.Children.Add(view);
            rows.Children.Add(actions);
            rows.Children.Add(preview);
            if (string.IsNullOrEmpty(capture.RawImagePath) || !File.Exists(capture.RawImagePath))
                rows.Children.Add(new TextBlock { Text = "Reference capture only. The note is preserved; no clean frame is available to restore its overlay.",
                    TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            notesList.Children.Add(new Border { Padding = new Thickness(10), BorderThickness = new Thickness(1),
                BorderBrush = isResolved ? Brushes.Gray : FigureBrush, CornerRadius = new CornerRadius(6), Child = rows });
        }
    }

    internal void ProcessCommands()
    {
        if (!loaded || IsEditingNote) return;
        var directory = Path.Combine(store.OutputDirectory, "commands");
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Take(50))
        {
            try
            {
                var command = JsonSerializer.Deserialize(File.ReadAllText(path), RegionReviewJsonContext.Default.RegionReviewCommand)
                    ?? throw new InvalidDataException("Empty review command.");
                if (!Guid.TryParseExact(command.Id, "N", out _) || Path.GetFileNameWithoutExtension(path) != command.Id)
                    throw new InvalidDataException("Invalid review command ID.");
                var valid = command.SessionId == store.SessionId && command.Status is "resolved" or "open";
                var applied = valid && SetResolved(command.FigureId, command.Status == "resolved", command.ResolutionNote);
                var receipt = new RegionReviewReceipt { Applied = applied,
                    Error = applied ? null : "Session, figure, status, or persistence check failed." };
                var receiptPath = Path.Combine(directory, command.Id + ".result");
                File.WriteAllText(receiptPath + ".tmp",
                    JsonSerializer.Serialize(receipt, RegionReviewJsonContext.Default.RegionReviewReceipt));
                File.Move(receiptPath + ".tmp", receiptPath, true);
                File.Delete(path);
            }
            catch (Exception exception)
            {
                failureMessage = $"Review command failed: {exception.Message}";
                InvalidateVisual();
            }
        }
    }
}
#endif
