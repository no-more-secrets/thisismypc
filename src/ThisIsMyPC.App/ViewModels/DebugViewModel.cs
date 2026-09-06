using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Notifications;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>One entry in a simulation dropdown; a null value means "real".</summary>
public sealed record SimulationOption<T>(string DisplayName, T? Value) where T : struct
{
    public override string ToString() => DisplayName;
}

/// <summary>One entry in the device dropdown; a null preset means "real".</summary>
public sealed record DeviceOption(string DisplayName, SimulatedDevice? Preset)
{
    public override string ToString() => DisplayName;
}

/// <summary>A row on the State Simulation tab for one hardware ecosystem.</summary>
public sealed partial class CapabilitySimulationViewModel : ViewModelBase
{
    private readonly DebugSimulation _simulation;

    public SystemCapability Capability { get; }
    public string DisplayName { get; }
    public IReadOnlyList<SimulationOption<bool>> Options { get; } =
    [
        new("Real", null),
        new("Present", true),
        new("Missing", false),
    ];

    [ObservableProperty]
    private SimulationOption<bool> _selected;

    public CapabilitySimulationViewModel(DebugSimulation simulation, SystemCapability capability)
    {
        _simulation = simulation;
        Capability = capability;
        DisplayName = DebugSimulation.CapabilityName(capability);
        _selected = Options.First(o => o.Value == simulation.GetCapability(capability));
    }

    partial void OnSelectedChanged(SimulationOption<bool> value) => _simulation.SetCapability(Capability, value.Value);

    internal void Sync() => Selected = Options.First(o => o.Value == _simulation.GetCapability(Capability));
}

/// <summary>
/// The Debug page (Debug builds only): the UI Gallery, test controls that
/// fire real app surfaces on an explicit click, and state simulation that
/// changes what the app believes about the PC without touching Windows.
/// </summary>
public sealed partial class DebugViewModel : ViewModelBase, ITabbedPage, IDisposable
{
    public const string Title = "Debug";

    private readonly INotificationService? _notifications;
    private readonly IExplorerRestartService _explorerRestart;
    private readonly DebugSimulation _simulation;
    private readonly Action<string, string, ToastSeverity> _showToast;
    private readonly Action<RestartRequirement> _showRestartBanner;
    private readonly Action<ChangeCategory> _stageSampleChange;
    private bool _syncing;

    [ObservableProperty]
    private int _selectedTabIndex;

    public GalleryViewModel Gallery { get; } = new();

    public DebugViewModel(
        DebugSimulation simulation,
        IExplorerRestartService explorerRestart,
        INotificationService? notifications,
        Action<string, string, ToastSeverity> showToast,
        Action<RestartRequirement> showRestartBanner,
        Action<ChangeCategory> stageSampleChange)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(explorerRestart);
        ArgumentNullException.ThrowIfNull(showToast);
        ArgumentNullException.ThrowIfNull(showRestartBanner);
        ArgumentNullException.ThrowIfNull(stageSampleChange);
        _simulation = simulation;
        _explorerRestart = explorerRestart;
        _notifications = notifications;
        _showToast = showToast;
        _showRestartBanner = showRestartBanner;
        _stageSampleChange = stageSampleChange;

        SkuOptions =
        [
            new("Real (detected)", null),
            new("Windows 11 Home", WindowsSku.Home),
            new("Windows 11 Pro", WindowsSku.Pro),
            new("Windows 11 Enterprise", WindowsSku.Enterprise),
            new("Windows 11 Education", WindowsSku.Education),
        ];
        DeviceOptions =
        [
            new DeviceOption("Real (detected)", null),
            .. DebugSimulation.DevicePresets.Select(p => new DeviceOption(p.DisplayName, p)),
        ];
        OwnerModeOptions =
        [
            new("Real (queried)", null),
            new("Running", OwnerModeState.Running),
            new("Installed, not running", OwnerModeState.Stopped),
            new("Installed, disabled", OwnerModeState.Disabled),
            new("Not installed", OwnerModeState.NotInstalled),
        ];
        Capabilities = DebugSimulation.OverridableCapabilities
            .Select(c => new CapabilitySimulationViewModel(simulation, c))
            .ToList();

        _selectedSku = SkuOptions.First(o => o.Value == simulation.Sku);
        _selectedDevice = DeviceOptions.First(o => o.Preset == simulation.Device);
        _selectedOwnerMode = OwnerModeOptions.First(o => o.Value == simulation.OwnerModeState);
        _simulation.Changed += OnSimulationChanged;
        RefreshSimulationText();
    }

    // --- Test Controls ---

    [ObservableProperty]
    private string _testControlsResult = string.Empty;

    /// <summary>Sends through the real notification service, so the Notifications settings gate it like any other.</summary>
    [RelayCommand]
    private void SendTestNotification()
    {
        if (_notifications is null)
        {
            TestControlsResult = "No notification service in this session.";
            return;
        }

        var raised = _notifications.Notify(
            NotificationType.Monitoring,
            "Test notification",
            $"Sent from the Debug page at {DateTime.Now:HH:mm:ss}.");
        TestControlsResult = raised
            ? "Raised. It shows as a toast, and as a Windows notification when the app is in the tray."
            : "Blocked by your settings: Notifications or Notify: monitoring alerts is off. The app treats a test exactly like a real alert.";
    }

    [RelayCommand]
    private void ShowInfoToast()
    {
        _showToast("Test toast", $"Information at {DateTime.Now:HH:mm:ss}.", ToastSeverity.Info);
        TestControlsResult = "Info toast shown top-right.";
    }

    [RelayCommand]
    private void ShowWarningToast()
    {
        _showToast("Test warning", $"Warning at {DateTime.Now:HH:mm:ss}.", ToastSeverity.Warning);
        TestControlsResult = "Warning toast shown top-right.";
    }

    [ObservableProperty]
    private bool _isRestartingExplorer;

    /// <summary>The real Explorer restart, on this click only. Open File Explorer windows close.</summary>
    [RelayCommand]
    private async Task RestartExplorerAsync()
    {
        if (IsRestartingExplorer)
            return;
        // A real restart is a mutation like any other: refused under simulation,
        // and the simulation cannot switch on while it runs.
        using var lease = _simulation.BeginMutation();
        if (lease.Refusal is { } refusal)
        {
            TestControlsResult = refusal;
            return;
        }
        IsRestartingExplorer = true;
        TestControlsResult = "Restarting Explorer...";
        try
        {
            var result = await _explorerRestart.RestartExplorerAsync().ConfigureAwait(true);
            TestControlsResult = result.IsSuccess
                ? "Explorer restarted."
                : $"Explorer restart failed: {result.ErrorMessage}";
        }
        finally
        {
            IsRestartingExplorer = false;
        }
    }

    [RelayCommand]
    private void ShowExplorerRestartBanner()
    {
        _showRestartBanner(RestartRequirement.ExplorerRestart);
        TestControlsResult = "Explorer restart banner shown above the apply bar. Its button restarts Explorer for real.";
    }

    [RelayCommand]
    private void ShowRebootBanner()
    {
        _showRestartBanner(RestartRequirement.Reboot);
        TestControlsResult = "Reboot banner shown above the apply bar.";
    }

    [RelayCommand]
    private void ShowSignOutBanner()
    {
        _showRestartBanner(RestartRequirement.SignOut);
        TestControlsResult = "Sign-out banner shown above the apply bar.";
    }

    [RelayCommand]
    private void StageSampleEnable() => StageSample(ChangeCategory.Enable);

    [RelayCommand]
    private void StageSampleDisable() => StageSample(ChangeCategory.Disable);

    [RelayCommand]
    private void StageSampleModify() => StageSample(ChangeCategory.Modify);

    private void StageSample(ChangeCategory category)
    {
        _stageSampleChange(category);
        TestControlsResult = $"Sample {category.ToString().ToLowerInvariant()} change staged. Apply fails on purpose: the sample module does not exist, so the review panel shows a failed group.";
    }

    // --- State Simulation ---

    public IReadOnlyList<SimulationOption<WindowsSku>> SkuOptions { get; }
    public IReadOnlyList<DeviceOption> DeviceOptions { get; }
    public IReadOnlyList<SimulationOption<OwnerModeState>> OwnerModeOptions { get; }
    public IReadOnlyList<CapabilitySimulationViewModel> Capabilities { get; }

    [ObservableProperty]
    private SimulationOption<WindowsSku> _selectedSku;

    [ObservableProperty]
    private DeviceOption _selectedDevice;

    [ObservableProperty]
    private SimulationOption<OwnerModeState> _selectedOwnerMode;

    [ObservableProperty]
    private bool _isSimulationActive;

    [ObservableProperty]
    private string _simulationSummary = string.Empty;

    public static string SimulationNote =>
        "Simulation changes what the app believes about this PC, not Windows. It does not test real hardware, "
        + "editions, or Windows enforcement. While anything here is set, Apply, installs, and Owner Mode service "
        + "actions are refused, and every open page reloads with the simulated state. Nothing here is saved; "
        + "closing the app resets it.";

    partial void OnSelectedSkuChanged(SimulationOption<WindowsSku> value)
    {
        if (!_syncing)
            _simulation.Sku = value.Value;
    }

    partial void OnSelectedDeviceChanged(DeviceOption value)
    {
        if (!_syncing)
            _simulation.Device = value.Preset;
    }

    partial void OnSelectedOwnerModeChanged(SimulationOption<OwnerModeState> value)
    {
        if (!_syncing)
            _simulation.OwnerModeState = value.Value;
    }

    [RelayCommand]
    private void ResetSimulation() => _simulation.Reset();

    private void OnSimulationChanged(object? sender, EventArgs e)
    {
        _syncing = true;
        try
        {
            SelectedSku = SkuOptions.First(o => o.Value == _simulation.Sku);
            SelectedDevice = DeviceOptions.First(o => o.Preset == _simulation.Device);
            SelectedOwnerMode = OwnerModeOptions.First(o => o.Value == _simulation.OwnerModeState);
            foreach (var capability in Capabilities)
                capability.Sync();
        }
        finally
        {
            _syncing = false;
        }

        RefreshSimulationText();
    }

    private void RefreshSimulationText()
    {
        IsSimulationActive = _simulation.IsActive;
        SimulationSummary = _simulation.LastRefusal is { } refusal
            ? refusal
            : _simulation.IsActive
                ? $"Simulated: {_simulation.Summary}."
                : "Nothing simulated. The app shows what it detected.";
    }

    public void Dispose() => _simulation.Changed -= OnSimulationChanged;
}
