using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.Views;

public partial class PathEditorView : UserControl
{
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x9E, 0xFF));

    private Control? _dragSourceRow;
    private Border? _dragGhost;
    private Border? _insertLine;
    private int _insertGap = -1;

    public PathEditorView()
    {
        InitializeComponent();
        // handledEventsToo: true ensures we receive events even when TextBox handles them
        AddHandler(DragDrop.DropEvent, OnDrop,
            RoutingStrategies.Direct | RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver,
            RoutingStrategies.Direct | RoutingStrategies.Bubble, true);
    }

    private async void OnGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return;
        if (sender is not Control { DataContext: PathEntryViewModel entry })
            return;
        if (DataContext is not PathEditorViewModel vm)
            return;

        var fromIndex = vm.Entries.IndexOf(entry);

        // Dim the source row
        _dragSourceRow = FindRowBorder(sender as Control);
        if (_dragSourceRow is not null)
            _dragSourceRow.Opacity = 0.3;

        // Show ghost preview
        ShowGhost(entry, e.GetPosition(DragOverlay));

#pragma warning disable CS0618 // Avalonia 11.3.x DataTransfer replacement not yet stable
        var data = new DataObject();
        data.Set("PathEntryIndex", fromIndex);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
#pragma warning restore CS0618

        // Cleanup after drag completes or cancels
        if (_dragSourceRow is not null)
        {
            _dragSourceRow.Opacity = 1.0;
            _dragSourceRow = null;
        }

        HideGhost();
        HideInsertLine();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        if (!e.Data.Contains("PathEntryIndex"))
#pragma warning restore CS0618
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Move;

        // Move ghost to follow cursor
        if (_dragGhost is not null)
        {
            var pos = e.GetPosition(DragOverlay);
            Canvas.SetTop(_dragGhost, pos.Y - _dragGhost.Bounds.Height / 2);
        }

        // Calculate insertion gap from pointer position
        var targetBorder = FindRowBorder(e.Source as Control);
        if (targetBorder is null)
            return;

        var targetEntry = FindEntryFromVisual(e.Source as Control);
        if (targetEntry is null || DataContext is not PathEditorViewModel vm)
            return;

        var targetIndex = vm.Entries.IndexOf(targetEntry);
        var posInRow = e.GetPosition(targetBorder);
        var gap = posInRow.Y < targetBorder.Bounds.Height / 2
            ? targetIndex       // top half: insert before this row
            : targetIndex + 1;  // bottom half: insert after this row

        if (gap != _insertGap)
        {
            _insertGap = gap;
            ShowInsertLine(targetBorder, gap == targetIndex);
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not PathEditorViewModel vm)
            return;
#pragma warning disable CS0618
        if (e.Data.Get("PathEntryIndex") is not int fromIndex)
#pragma warning restore CS0618
            return;

        if (_insertGap >= 0)
        {
            // Use the calculated insertion gap
            var toIndex = fromIndex < _insertGap ? _insertGap - 1 : _insertGap;
            vm.MoveEntry(fromIndex, toIndex);
        }
        else
        {
            // Fallback: resolve target directly from drop position
            var target = FindEntryFromVisual(e.Source as Control);
            if (target is null)
                return;
            var toIndex = vm.Entries.IndexOf(target);
            if (toIndex >= 0)
                vm.MoveEntry(fromIndex, toIndex);
        }
    }

    private void ShowGhost(PathEntryViewModel entry, Point position)
    {
        _dragGhost = new Border
        {
            Opacity = 0.5,
            Padding = new Thickness(8, 4),
            CornerRadius = new CornerRadius(4),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x22, 0x22, 0x2E)),
            IsHitTestVisible = false,
            Width = EntriesList.Bounds.Width,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = entry.Index.ToString(),
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x88)),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = entry.Path,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0xAA)),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };

        Canvas.SetLeft(_dragGhost, 0);
        Canvas.SetTop(_dragGhost, position.Y - 12);
        DragOverlay.Children.Add(_dragGhost);
    }

    private void HideGhost()
    {
        if (_dragGhost is not null)
        {
            DragOverlay.Children.Remove(_dragGhost);
            _dragGhost = null;
        }
    }

    private void ShowInsertLine(Control targetBorder, bool aboveTarget)
    {
        var rowPos = targetBorder.TranslatePoint(new Point(0, 0), DragOverlay);
        if (rowPos is null)
            return;

        var lineY = aboveTarget
            ? rowPos.Value.Y
            : rowPos.Value.Y + targetBorder.Bounds.Height;

        if (_insertLine is null)
        {
            _insertLine = new Border
            {
                Height = 2,
                Background = AccentBrush,
                IsHitTestVisible = false,
                Width = EntriesList.Bounds.Width,
            };
            DragOverlay.Children.Add(_insertLine);
        }

        Canvas.SetLeft(_insertLine, 0);
        Canvas.SetTop(_insertLine, lineY - 1);
    }

    private void HideInsertLine()
    {
        if (_insertLine is not null)
        {
            DragOverlay.Children.Remove(_insertLine);
            _insertLine = null;
        }

        _insertGap = -1;
    }

    private static Border? FindRowBorder(Control? control)
    {
        while (control is not null)
        {
            if (control is Border b && DragDrop.GetAllowDrop(b))
                return b;
            control = control.Parent as Control;
        }

        return null;
    }

    private static PathEntryViewModel? FindEntryFromVisual(Control? control)
    {
        while (control is not null)
        {
            if (control.DataContext is PathEntryViewModel entry)
                return entry;
            control = control.Parent as Control;
        }

        return null;
    }
}
