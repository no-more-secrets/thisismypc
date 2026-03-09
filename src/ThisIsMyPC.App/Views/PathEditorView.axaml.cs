using Avalonia.Controls;
using Avalonia.Input;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.Views;

public partial class PathEditorView : UserControl
{
    public PathEditorView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private async void OnGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return;
        if (sender is not Control { DataContext: PathEntryViewModel entry })
            return;
        if (DataContext is not PathEditorViewModel vm)
            return;

        var data = new DataObject();
        data.Set("PathEntryIndex", vm.Entries.IndexOf(entry));
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains("PathEntryIndex")
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not PathEditorViewModel vm)
            return;
        if (e.Data.Get("PathEntryIndex") is not int fromIndex)
            return;

        var target = FindEntryFromVisual(e.Source as Control);
        if (target is null)
            return;

        var toIndex = vm.Entries.IndexOf(target);
        if (toIndex >= 0)
            vm.MoveEntry(fromIndex, toIndex);
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
