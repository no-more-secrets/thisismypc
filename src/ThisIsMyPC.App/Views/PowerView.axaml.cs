using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace ThisIsMyPC.App.Views;

public partial class PowerView : UserControl
{
    public PowerView()
    {
        InitializeComponent();
    }

    /// <summary>A row of the Add plan dropdown was pressed: its command ran; close the dropdown.</summary>
    private void OnAddPlanItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control item)
            return;
        var presenter = item.FindLogicalAncestorOfType<FlyoutPresenter>();
        if (presenter?.Parent is Popup popup)
            popup.IsOpen = false;
        else
            AddPlanButton.Flyout?.Hide();
    }
}
