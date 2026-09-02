using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ThisIsMyPC.App.Views;

public partial class PowerView : UserControl
{
    public PowerView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// A row of the Add plan dropdown was pressed. Button raises Click before
    /// it runs the row's Command, and hiding the flyout detaches its content
    /// (the command binding with it), so the close is posted to run after the
    /// command.
    /// </summary>
    private void OnAddPlanItemClick(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => AddPlanButton.Flyout?.Hide(), DispatcherPriority.Input);
    }
}
