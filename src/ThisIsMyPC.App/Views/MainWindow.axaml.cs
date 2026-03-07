using Avalonia;
using Avalonia.Controls;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.Views;

public partial class MainWindow : Window
{
    private const double CollapseThreshold = 1100;
    private bool _wasAboveThreshold = true;

    public MainWindow()
    {
        InitializeComponent();

        PropertyChanged += OnWindowPropertyChanged;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            _wasAboveThreshold = Bounds.Width >= CollapseThreshold;
            await vm.InitializeAsync().ConfigureAwait(true);
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != BoundsProperty || DataContext is not MainWindowViewModel vm)
            return;

        var isAboveThreshold = Bounds.Width >= CollapseThreshold;

        // Only act on threshold crossings
        if (isAboveThreshold == _wasAboveThreshold)
            return;

        _wasAboveThreshold = isAboveThreshold;

        vm.IsSidebarCollapsed = !isAboveThreshold;
    }
}
