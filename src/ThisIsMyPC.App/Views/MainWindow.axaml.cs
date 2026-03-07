using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.Views;

public partial class MainWindow : Window
{
    private const double CollapseThreshold = 1100;
    private bool _wasAboveThreshold = true;
#if DEBUG
    private int _debugChangeCounter;
#endif

    public MainWindow()
    {
        InitializeComponent();

        PropertyChanged += OnWindowPropertyChanged;
        Loaded += OnLoaded;
#if DEBUG
        KeyDown += OnDebugKeyDown;
#endif
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

#if DEBUG
    private void OnDebugKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        // F5: Stage a test enable change
        // F6: Stage a test disable change
        // F7: Stage a test modify change
        // F8: Discard all
        var category = e.Key switch
        {
            Key.F5 => Core.Changes.ChangeCategory.Enable,
            Key.F6 => Core.Changes.ChangeCategory.Disable,
            Key.F7 => Core.Changes.ChangeCategory.Modify,
            Key.F8 => (Core.Changes.ChangeCategory?)null,
            _ => (Core.Changes.ChangeCategory?)(-1),
        };

        if (category == (Core.Changes.ChangeCategory?)(-1))
            return;

        if (e.Key == Key.F8)
        {
            vm.DiscardAllCommand.Execute(null);
            return;
        }

        _debugChangeCounter++;
        var change = new Core.Changes.ChangeDescriptor
        {
            ModuleId = "DebugModule",
            SettingId = $"debug-setting-{_debugChangeCounter}",
            DisplayName = $"Test Setting {_debugChangeCounter}",
            SystemLocation = @$"HKLM\SOFTWARE\Debug\Setting{_debugChangeCounter}",
            BeforeValue = "0",
            AfterValue = "1",
            BeforeDisplay = category == Core.Changes.ChangeCategory.Enable ? "Disabled" : category == Core.Changes.ChangeCategory.Disable ? "Enabled" : "Value A",
            AfterDisplay = category == Core.Changes.ChangeCategory.Enable ? "Enabled" : category == Core.Changes.ChangeCategory.Disable ? "Disabled" : "Value B",
            ValueType = Core.Changes.ChangeValueType.Registry_DWord,
            Category = category!.Value,
        };

        vm.StageDebugChange(change);
    }
#endif
}
