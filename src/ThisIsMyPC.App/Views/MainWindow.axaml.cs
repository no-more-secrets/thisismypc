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
        AddHandler(PointerPressedEvent, OnGlobalPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnSearchKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        HookDisplayChanges();
#if DEBUG
        KeyDown += OnDebugKeyDown;
#endif
    }

    /// <summary>
    /// Clicking empty space releases keyboard focus. Avalonia only moves focus
    /// when the click lands on a focusable control, so without this a search
    /// box stays highlighted and keeps eating keystrokes after clicking away.
    /// </summary>
    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source)
            return;

        var hitFocusable = false;
        var hitSearch = false;
        var hitAbout = false;
        for (Visual? v = source; v is not null; v = Avalonia.VisualTree.VisualExtensions.GetVisualParent(v))
        {
            if (v is IInputElement { Focusable: true })
                hitFocusable = true;
            if (v == SearchBox || v == SearchResultsPanel)
                hitSearch = true;
            // The info button toggles About itself; closing it here too would reopen it.
            if (v == AboutPanel || v == AboutButton)
                hitAbout = true;
        }

        if (!hitFocusable)
            FocusManager?.ClearFocus();

        // A click anywhere but an overlay or its own button closes that overlay.
        if (DataContext is MainWindowViewModel vm)
        {
            if (!hitSearch)
                vm.IsSearchOpen = false;
            if (!hitAbout)
                vm.IsAboutOpen = false;
        }
    }

    // --- Sidebar grip: drag the sidebar edge between its two widths ---

    private bool _gripDragging;

    private void OnSidebarGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _gripDragging = true;
        e.Pointer.Capture(SidebarGrip);
        e.Handled = true;
    }

    private void OnSidebarGripMoved(object? sender, PointerEventArgs e)
    {
        if (!_gripDragging || DataContext is not MainWindowViewModel vm)
            return;
        // Snap as the pointer crosses the midpoint between the two widths.
        var x = e.GetPosition(this).X;
        var midpoint = (SidebarWidthConverter.CollapsedWidth + SidebarWidthConverter.ExpandedWidth) / 2;
        vm.IsSidebarCollapsed = x < midpoint;
    }

    private void OnSidebarGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_gripDragging)
            return;
        _gripDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not MainWindowViewModel vm)
            return;
        if (!vm.IsSearchOpen && !vm.IsAboutOpen)
            return;
        vm.IsSearchOpen = false;
        vm.IsAboutOpen = false;
        e.Handled = true;
    }

    // --- Resume/display-change watch: monitors forget DDC state across sleep ---

    private int _reapplyScheduled;

    private void HookDisplayChanges()
    {
        try
        {
            Win32Properties.AddWndProcHookCallback(this, DisplayWndProcHook);
        }
        catch (InvalidOperationException)
        {
            // Headless/test platforms have no Win32 window; the feature is
            // desktop-only and everything else works without it.
        }
    }

    private nint DisplayWndProcHook(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        const uint WM_DISPLAYCHANGE = 0x007E;
        const uint WM_POWERBROADCAST = 0x0218;
        const int PBT_APMRESUMEAUTOMATIC = 0x0012;
        const int PBT_APMRESUMESUSPEND = 0x0007;

        var isResume = msg == WM_POWERBROADCAST
            && wParam is PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMESUSPEND;
        if (msg == WM_DISPLAYCHANGE || isResume)
            ScheduleDisplayReapply();

        return 0;
    }

    private async void ScheduleDisplayReapply()
    {
        // One pending re-apply at a time; a burst of messages (resume fires
        // several) collapses into a single delayed pass.
        if (System.Threading.Interlocked.Exchange(ref _reapplyScheduled, 1) == 1)
            return;

        try
        {
            // Monitors need a moment after wake before DDC answers.
            await System.Threading.Tasks.Task.Delay(4000);
            if (DataContext is MainWindowViewModel vm)
                await vm.HandleDisplayTopologyChangedAsync();
        }
        finally
        {
            _reapplyScheduled = 0;
        }
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
