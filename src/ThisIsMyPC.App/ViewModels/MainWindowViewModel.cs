using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Hardware;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Shell.Models;
using ContextMenuHandlerList = System.Collections.Generic.IReadOnlyList<ThisIsMyPC.Modules.Shell.Models.ContextMenuHandler>;

namespace ThisIsMyPC.App.ViewModels;

public enum StatusSeverity
{
    Success,
    Warning,
    Error,
}

public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.App.ViewModels.MainWindowViewModel");

    private readonly NavigationService _navigationService;
    private readonly IPendingChangesService _pendingChangesService;
    private readonly Core.Drift.DeliberateChangeCoordinator? _deliberateChanges;
    private readonly IPendingActionsService? _pendingActionsService;
    private readonly Core.Packages.IWingetService? _wingetService;
    private readonly Services.AutorunEnrichment? _autorunEnrichment;
    private readonly IChangeHistoryService _changeHistoryService;
    private readonly IRegistryService _registryService;
    private readonly IPowerService? _powerService;
    private readonly IMonitorService? _monitorService;
    private readonly DisplayModePreferencesStore? _displayModeStore;
    private readonly Services.OwnerModeService? _ownerModeService;
    private readonly Ipc.Contracts.IIpcClient? _ipcClient;
    private DriftSectionViewModel? _driftSection;
    private readonly IExplorerRestartService _explorerRestartService;
    private readonly Core.Sets.ISetProvider _setProvider;
    private readonly IReadOnlyList<Core.Sets.ISetEntryInspector> _setEntryInspectors;
    private readonly ICapabilityDetector? _capabilityDetector;
    private readonly Core.Settings.ISettingsService? _settingsService;
    private readonly IReadOnlyList<Core.Settings.IModuleSettingsContributor> _moduleSettingsContributors;
    private readonly IUpdateService? _updateService;
    private readonly IReadOnlyList<Core.Search.ISearchSettingsContributor> _searchContributors;
    private Core.Search.SettingsSearchService? _searchService;
    private readonly Core.Notifications.INotificationService? _notificationService;
    private readonly Core.Monitoring.MonitoringService? _monitoringService;
    private readonly IRestorePointService _restorePointService;
    private readonly IPrivilegeBrokerClient? _privilegeBroker;
    private readonly Services.HardwareCompanionActions? _hardwareActions;
    private readonly Core.Hardware.Lighting.ILightingBackend? _lightingBackend;
    private IPrivilegeBrokerSession? _activeBrokerSession;

    // --- 9-3 monitoring review (Home section) ---

    private MonitoringSectionViewModel? BuildMonitoringSection()
    {
        if (_monitoringService is null)
            return null;
        var detections = _monitoringService.UnreviewedDetections;
        if (detections.Count == 0)
            return null;

        return new MonitoringSectionViewModel(detections
            .Select(d => new DetectionRowViewModel(d, DisableDetection, DismissDetection))
            .ToList());
    }

    private void DisableDetection(DetectionRowViewModel row)
    {
        if (RefuseStagingWhileUnresolved())
            return;
        var inspector = _setEntryInspectors.FirstOrDefault(i => i.ModuleId == "Startup & Services");
        if (inspector is null)
        {
            SetStatus("The Startup & Services module is not available to stage this change.", StatusSeverity.Warning);
            return;
        }

        var entry = new Core.Sets.SetEntry
        {
            ModuleId = "Startup & Services",
            SettingId = row.Detection.Id,
            Value = DisableValueFor(row.Detection.Id),
            Description = $"Disable detected item: {row.Detection.DisplayName}",
        };

        var group = inspector.CreateChangeGroup(entry);
        if (group is null)
        {
            SetStatus($"\"{row.Detection.DisplayName}\" could not be resolved (it may have been removed already).", StatusSeverity.Warning);
            _monitoringService?.MarkReviewed(row.Detection.Id);
            RefreshMonitoringSection();
            return;
        }

        _pendingChangesService.Stage(group);
        _monitoringService?.MarkReviewed(row.Detection.Id);
        SetStatus($"Disable staged for \"{row.Detection.DisplayName}\" - review and apply when ready.", StatusSeverity.Success);
        RefreshMonitoringSection();
    }

    private void DismissDetection(DetectionRowViewModel row)
    {
        _monitoringService?.MarkReviewed(row.Detection.Id);
        RefreshMonitoringSection();
    }

    private void RefreshMonitoringSection()
    {
        if (CurrentContent is HomeViewModel && IsHomeActive)
            OpenHome();
    }

    // New detection while the user sits on Home (the normal tray-idle case) must
    // surface without requiring navigation churn.
    private void OnDetectionsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            RefreshMonitoringSection();
        else
            Dispatcher.UIThread.Post(RefreshMonitoringSection);
    }

    /// <summary>Disable value per the Startup inspector's settingId conventions.</summary>
    internal static string DisableValueFor(string settingId) =>
        settingId.StartsWith("startup-entry:", StringComparison.Ordinal)
            ? Convert.ToHexString(Modules.Startup.Changes.StartupChangeFactory.DisabledBlob)
            : "Disabled";

    // --- 28-3 drift report (Home section) ---

    /// <summary>
    /// One fetch at startup: a missing/stopped service degrades silently (Owner Mode
    /// is optional). Detected drift is audited into history once per report and
    /// surfaced on Home with reapply options.
    /// </summary>
    public async Task LoadDriftReportAsync()
    {
        if (_ipcClient is null)
            return;

        var report = await _ipcClient.GetDriftReportAsync().ConfigureAwait(true);
        if (!report.IsSuccess || report.Value is not { Items.Count: > 0 } value)
            return;

        _driftSection = new DriftSectionViewModel(
            value.Items,
            ReapplyDriftItem,
            dismissed: () =>
            {
                _driftSection = null;
                RefreshMonitoringSection();
            });

        await RecordDriftHistoryOnceAsync(value).ConfigureAwait(true);
        RefreshMonitoringSection();
    }

    private void ReapplyDriftItem(DriftRowViewModel row)
    {
        if (RefuseStagingWhileUnresolved())
            return;
        if (!Enum.TryParse<ChangeValueType>(row.Item.ValueType, out var valueType))
        {
            SetStatus($"\"{row.Item.DisplayName}\" has an unrecognized value type and cannot be restaged.", StatusSeverity.Warning);
            return;
        }

        _pendingChangesService.Stage(Core.Drift.DriftReapplyFactory.CreateReapply(
            row.Item.ModuleId, row.Item.SettingId, row.Item.DisplayName, row.Item.SystemLocation,
            valueType, row.Item.ExpectedValue, row.Item.CurrentValue, row.Item.EnforcementJson));
        SetStatus($"Reapply staged for \"{row.Item.DisplayName}\" - review and apply when ready.", StatusSeverity.Success);
    }

    /// <summary>Audits each drift report into history exactly once (keyed on GeneratedAtUtc).</summary>
    private async Task RecordDriftHistoryOnceAsync(Ipc.Contracts.DriftReportResponse report)
    {
        var stamp = report.GeneratedAtUtc?.ToString("O") ?? "";
        if (stamp.Length == 0 || _settingsService is null)
            return;
        if (_settingsService.GetApp(Core.Settings.AppSettingKeys.DriftLastRecorded, "") == stamp)
            return;

        var groupId = Guid.NewGuid().ToString("N");
        var entries = report.Items
            .Where(i => Enum.TryParse<ChangeValueType>(i.ValueType, out _))
            .Select(i => Core.Drift.DriftReapplyFactory.CreateDriftHistoryEntry(
                i.ModuleId, i.SettingId, i.DisplayName, i.SystemLocation,
                Enum.Parse<ChangeValueType>(i.ValueType), i.ExpectedValue, i.CurrentValue,
                groupId, report.GeneratedAtUtc!.Value, i.SuspectedCause))
            .ToList();

        await _changeHistoryService.RecordDriftEventsAsync(entries).ConfigureAwait(true);
        _settingsService.SetApp(Core.Settings.AppSettingKeys.DriftLastRecorded, stamp);
    }

    /// <summary>In-app toast stack rendered top-right over the content area.</summary>
    public ToastStackViewModel ToastStack { get; } = new();

    // 9-2: gated notifications surface as in-app toasts (monitoring detections
    // warn; the rest inform). The status bar stays reserved for the apply pipeline.
    private void OnNotificationRaised(object? sender, Core.Notifications.AppNotification notification)
    {
        var severity = notification.Type == Core.Notifications.NotificationType.Monitoring
            ? ToastSeverity.Warning
            : ToastSeverity.Info;
        Log.Info("Toast ({Type}): {Title}: {Message}", notification.Type, notification.Title, notification.Message);

        if (Dispatcher.UIThread.CheckAccess())
            ToastStack.Show(notification.Title, notification.Message, severity);
        else
            Dispatcher.UIThread.Post(() =>
                ToastStack.Show(notification.Title, notification.Message, severity));
    }

    // FR64: auto restore point when a batch stages this many individual changes
    private const int AutoRestorePointThreshold = 5;

    // Set when the pre-apply restore point failed and the user was told the next
    // Apply click proceeds without one. Reset on success or Discard All.
    private bool _applyWithoutRestorePoint;

    // Bumped on every content switch (module navigation, Home, Set Loader) so an
    // in-flight module scan can detect it was superseded and must not clobber the
    // content the user switched to meanwhile.
    private int _contentEpoch;

    public ObservableCollection<SidebarGroupViewModel> SidebarGroups { get; } = [];

    [ObservableProperty]
    private SidebarItemViewModel? _selectedModule;

    [ObservableProperty]
    private string _contentTitle = string.Empty;

    [ObservableProperty]
    private string _contentDescription = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesEdgeTabs))]
    private object? _currentContent;

    /// <summary>Whether the current page owns its card edge and content padding.</summary>
    public bool UsesEdgeTabs => CurrentContent is ShellViewModel or EnvironmentViewModel or SettingsViewModel
        or SoftwareViewModel or ContextMenuViewModel or StartupViewModel or PowerViewModel or SettingCardPageViewModel
        or DebugViewModel;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingChanges))]
    [NotifyPropertyChangedFor(nameof(PendingCountText))]
    [NotifyPropertyChangedFor(nameof(TotalPendingCount))]
    [NotifyPropertyChangedFor(nameof(CanModifyPending))]
    [NotifyPropertyChangedFor(nameof(CanApplyPending))]
    private int _pendingCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingChanges))]
    [NotifyPropertyChangedFor(nameof(PendingCountText))]
    [NotifyPropertyChangedFor(nameof(TotalPendingCount))]
    [NotifyPropertyChangedFor(nameof(CanModifyPending))]
    [NotifyPropertyChangedFor(nameof(CanApplyPending))]
    private int _actionCount;

    /// <summary>Changes plus one-way actions: what the Apply badge shows.</summary>
    public int TotalPendingCount => PendingCount + ActionCount;

    public bool HasPendingChanges => TotalPendingCount > 0;

    public string PendingCountText
    {
        get
        {
            if (PendingCount == 0 && ActionCount == 0)
                return "No pending changes";

            var parts = new List<string>();
            if (PendingCount > 0)
                parts.Add($"{PendingCount} change{(PendingCount == 1 ? "" : "s")}");
            if (ActionCount > 0)
                parts.Add($"{ActionCount} action{(ActionCount == 1 ? "" : "s")}");
            return string.Join(", ", parts) + " pending";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanModifyPending))]
    [NotifyPropertyChangedFor(nameof(CanApplyPending))]
    [NotifyPropertyChangedFor(nameof(CanCreateRestorePoint))]
    private bool _isApplying;

    public bool CanModifyPending => HasPendingChanges && !IsApplying;

    public bool CanCreateRestorePoint => !IsCreatingRestorePoint && !IsApplying;

    public ReviewPanelViewModel ReviewPanel { get; }

    public ChangeHistoryViewModel ChangeHistory { get; }

    [ObservableProperty]
    private bool _isHistoryPanelOpen;

    [ObservableProperty]
    private bool _isRestartNotificationVisible;

    [ObservableProperty]
    private string _restartNotificationMessage = string.Empty;

    [ObservableProperty]
    private bool _isRestartActionAvailable;

    [ObservableProperty]
    private bool _isRestartingExplorer;

    public MainWindowViewModel(
        NavigationService navigationService,
        IPendingChangesService pendingChangesService,
        IChangeHistoryService changeHistoryService,
        IRegistryService registryService,
        IExplorerRestartService explorerRestartService,
        ReviewPanelViewModel reviewPanel,
        Core.Sets.ISetProvider setProvider,
        IEnumerable<Core.Sets.ISetEntryInspector> setEntryInspectors,
        Core.Sets.ICustomSetWriter customSetWriter,
        IRestorePointService restorePointService,
        ICapabilityDetector? capabilityDetector = null,
        Core.Settings.ISettingsService? settingsService = null,
        IEnumerable<Core.Settings.IModuleSettingsContributor>? moduleSettingsContributors = null,
        IUpdateService? updateService = null,
        IEnumerable<Core.Search.ISearchSettingsContributor>? searchContributors = null,
        Core.Notifications.INotificationService? notificationService = null,
        Core.Monitoring.MonitoringService? monitoringService = null,
        IServiceControlService? serviceControlService = null,
        IScheduledTaskService? scheduledTaskService = null,
        Modules.Startup.Services.TaskClassificationOverrideStore? taskClassificationOverrides = null,
        IPowerService? powerService = null,
        IMonitorService? monitorService = null,
        DisplayModePreferencesStore? displayModeStore = null,
        Services.OwnerModeService? ownerModeService = null,
        Ipc.Contracts.IIpcClient? ipcClient = null,
        IPendingActionsService? pendingActionsService = null,
        Core.Packages.IWingetService? wingetService = null,
        Services.AutorunEnrichment? autorunEnrichment = null,
        Services.DebugSimulation? debugSimulation = null,
        Core.Drift.DeliberateChangeCoordinator? deliberateChanges = null,
        IPrivilegeBrokerClient? privilegeBroker = null,
        Core.Hardware.IHardwareDetectionService? hardwareDetection = null,
        Services.HardwareCompanionActions? hardwareActions = null,
        Core.Hardware.Lighting.ILightingBackend? lightingBackend = null)
    {
        _hardwareDetection = hardwareDetection;
        _privilegeBroker = privilegeBroker;
        _hardwareActions = hardwareActions;
        _lightingBackend = lightingBackend;
        _deliberateChanges = deliberateChanges;
        _wingetService = wingetService;
        _autorunEnrichment = autorunEnrichment;
        _pendingActionsService = pendingActionsService;
        _ownerModeService = ownerModeService;
        _debugSimulation = debugSimulation;
        _ownerModeControl = ownerModeService is null ? null
            : debugSimulation is null ? ownerModeService
            : new Services.SimulatedOwnerModeControl(ownerModeService, debugSimulation);
        if (_debugSimulation is not null)
            _debugSimulation.Changed += OnSimulationChanged;
        _ipcClient = ipcClient;
        _displayModeStore = displayModeStore;
        _powerService = powerService;
        _monitorService = monitorService;
        _navigationService = navigationService;
        _pendingChangesService = pendingChangesService;
        _changeHistoryService = changeHistoryService;
        _registryService = registryService;
        _explorerRestartService = explorerRestartService;
        _setProvider = setProvider;
        _setEntryInspectors = setEntryInspectors.ToList();
        _capabilityDetector = capabilityDetector;
        _settingsService = settingsService;
        _zoomPercent = int.TryParse(settingsService?.GetApp(Core.Settings.AppSettingKeys.UiZoom, "100"),
            System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var savedZoom)
            ? Math.Clamp(savedZoom, 50, 150) : 100;
        _moduleSettingsContributors = moduleSettingsContributors?.ToList() ?? [];
        _updateService = updateService;
        if (_settingsService is not null)
            _settingsService.SettingChanged += OnUpdateSettingChanged;
        _searchContributors = searchContributors?.ToList() ?? [];
        _notificationService = notificationService;
        _monitoringService = monitoringService;
        _restorePointService = restorePointService;
        if (_notificationService is not null)
            _notificationService.NotificationRaised += OnNotificationRaised;
        if (_monitoringService is not null)
            _monitoringService.DetectionsChanged += OnDetectionsChanged;
        ReviewPanel = reviewPanel;
        ChangeHistory = new ChangeHistoryViewModel(
            changeHistoryService,
            change => Dispatcher.UIThread.InvokeAsync(() => RevertChangeOnModule(change)),
            change => Dispatcher.UIThread.InvokeAsync(() => ApplyChangeToModule(change)),
            customSetWriter,
            BeginMutation,
            ipcClient is null ? null : async () =>
            {
                var response = await ipcClient.GetRestorationHistoryAsync().ConfigureAwait(false);
                return response.IsSuccess && response.Value?.Items is { } items ? items : [];
            });

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
        _navigationService.PropertyChanged += OnNavigationPropertyChanged;
        PendingCount = _pendingChangesService.PendingCount;
        RefreshUnresolvedGroups();
        if (_pendingActionsService is not null)
        {
            _pendingActionsService.PropertyChanged += OnPendingActionsPropertyChanged;
            ActionCount = _pendingActionsService.PendingCount;
        }
    }

    private void OnPendingActionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IPendingActionsService.PendingCount))
        {
            if (Dispatcher.UIThread.CheckAccess())
                ActionCount = _pendingActionsService!.PendingCount;
            else
                Dispatcher.UIThread.Post(() => ActionCount = _pendingActionsService!.PendingCount);
        }
    }

    private void OnPendingChangesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IPendingChangesService.PendingCount))
        {
            if (Dispatcher.UIThread.CheckAccess())
                PendingCount = _pendingChangesService.PendingCount;
            else
                Dispatcher.UIThread.Post(() => PendingCount = _pendingChangesService.PendingCount);
        }
        else if (e.PropertyName is nameof(IPendingChangesService.ReconciliationRequired)
            or nameof(IPendingChangesService.PendingGroups))
        {
            if (Dispatcher.UIThread.CheckAccess())
                RefreshUnresolvedGroups();
            else
                Dispatcher.UIThread.Post(RefreshUnresolvedGroups);
        }
    }

    private void RefreshUnresolvedGroups()
    {
        HasUnresolvedGroups = _pendingChangesService.ReconciliationRequired.Count > 0;
    }

    /// <summary>
    /// True while a staged group was left in an unknown state by an earlier
    /// apply. The queue refuses every batch until that group is discarded, so
    /// Apply All is off and the review panel says what to do instead.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyPending))]
    private bool _hasUnresolvedGroups;

    /// <summary>Apply All is available: something is staged, nothing is running, nothing is unresolved.</summary>
    public bool CanApplyPending => CanModifyPending && !HasUnresolvedGroups;

    // Modules whose page must be scanned again before it stages anything: a
    // change in one of them was left with an unknown live value, so the before
    // values its cards read at load may no longer describe the machine.
    private readonly HashSet<string> _modulesNeedingRescan = new(StringComparer.Ordinal);

    private async void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(NavigationService.CurrentModule))
            return;

        await LoadCurrentModuleAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Scans the current module and rebuilds its page from the result. Every
    /// card on the new page reads its before value from this scan, so this is
    /// also the reset after a group was discarded in an unknown state.
    /// </summary>
    private async Task LoadCurrentModuleAsync()
    {
        var epoch = 0;
        try
        {
            epoch = ++_contentEpoch;
            var current = _navigationService.CurrentModule;
            // Stamp before clearing or constructing content, including synchronous scans.
            if (_pendingSearchResult?.ModuleId == current?.Module.Info.Name)
                _pendingSearchFocusEpoch = epoch;
            else
                _pendingSearchResult = null;

            // Release the outgoing content VM's pending-changes subscriptions
            // before building the replacement (Dispose implementations are idempotent),
            // and show the loading state immediately: module scans read the live
            // system and can take a while.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                (CurrentContent as IDisposable)?.Dispose();
                CurrentContent = null;
                if (current is not null)
                {
                    ContentTitle = current.Module.Info.Name;
                    ContentDescription = current.Module.Info.Description;
                    LoadingText = $"Scanning {current.Module.Info.Name}...";
                    IsModuleLoading = true;
                }
            });

            if (current?.Module is ShellModule)
            {
                var scanResult = await current.Module.ScanSystemStateAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (epoch != _contentEpoch)
                        return; // superseded by Home/Set Loader/newer navigation while scanning
                    if (scanResult.IsSuccess && scanResult.Value is ShellScanData scanData)
                    {
                        ContentTitle = current.Module.Info.Name;
                        ContentDescription = current.Module.Info.Description;
                        CurrentContent = new ShellViewModel(scanData, _pendingChangesService, _registryService, _pendingActionsService);
                    }
                    else
                    {
                        CurrentContent = null;
                        SetStatus(scanResult.ErrorMessage ?? "Failed to scan shell settings", StatusSeverity.Error);
                    }
                });
            }
            else if (current?.Module is ContextMenuModule)
            {
                var scanResult = await current.Module.ScanSystemStateAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (epoch != _contentEpoch)
                        return; // superseded by Home/Set Loader/newer navigation while scanning
                    if (scanResult.IsSuccess && scanResult.Value is ContextMenuHandlerList handlers)
                    {
                        ContentTitle = current.Module.Info.Name;
                        ContentDescription = current.Module.Info.Description;
                        CurrentContent = new ContextMenuViewModel(handlers, _pendingChangesService, _registryService);
                    }
                    else
                    {
                        CurrentContent = null;
                        SetStatus(scanResult.ErrorMessage ?? "Failed to scan context menu handlers", StatusSeverity.Error);
                    }
                });
            }
            else if (current?.Module is Modules.WindowsUpdate.WindowsUpdateModule)
            {
                var scanResult = await current.Module.ScanSystemStateAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (epoch != _contentEpoch)
                        return; // superseded by Home/Set Loader/newer navigation while scanning
                    if (scanResult.IsSuccess && scanResult.Value is Modules.WindowsUpdate.Models.WindowsUpdateScanData updateData)
                    {
                        ContentTitle = current.Module.Info.Name;
                        ContentDescription = current.Module.Info.Description;
                        CurrentContent = new WindowsUpdateViewModel(
                            updateData, _pendingChangesService, _registryService,
                            _displayModeStore, _capabilityDetector, _ownerModeControl);
                    }
                    else
                    {
                        CurrentContent = null;
                        SetStatus(scanResult.ErrorMessage ?? "Failed to scan Windows Update policies", StatusSeverity.Error);
                    }
                });
            }
            else if (current?.Module is Modules.Privacy.PrivacyModule)
            {
                var scanResult = await current.Module.ScanSystemStateAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (epoch != _contentEpoch)
                        return; // superseded by Home/Set Loader/newer navigation while scanning
                    if (scanResult.IsSuccess && scanResult.Value is Modules.Privacy.Models.PrivacyScanData privacyData)
                    {
                        ContentTitle = current.Module.Info.Name;
                        ContentDescription = current.Module.Info.Description;
                        CurrentContent = new PrivacyViewModel(
                            privacyData, _pendingChangesService, _registryService,
                            _displayModeStore, _capabilityDetector, _ownerModeControl);
                    }
                    else
                    {
                        CurrentContent = null;
                        SetStatus(scanResult.ErrorMessage ?? "Failed to scan privacy settings", StatusSeverity.Error);
                    }
                });
            }
            else if (current?.Module is Modules.Annoyances.AnnoyancesModule)
            {
                var scanResult = await current.Module.ScanSystemStateAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (epoch != _contentEpoch)
                        return; // superseded by Home/Set Loader/newer navigation while scanning
                    if (scanResult.IsSuccess && scanResult.Value is Modules.Annoyances.Models.AnnoyancesScanData annoyancesData)
                    {
                        ContentTitle = current.Module.Info.Name;
                        ContentDescription = current.Module.Info.Description;
                        CurrentContent = new AnnoyancesViewModel(
                            annoyancesData, _pendingChangesService, _registryService,
                            _displayModeStore, _capabilityDetector, _ownerModeControl);
                    }
                    else
                    {
                        CurrentContent = null;
                        SetStatus(scanResult.ErrorMessage ?? "Failed to scan annoyance settings", StatusSeverity.Error);
                    }
                });
            }
            else if (current?.Module is Modules.Startup.StartupModule)
            {
                var scanResult = await current.Module.ScanSystemStateAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (epoch != _contentEpoch)
                        return; // superseded by Home/Set Loader/newer navigation while scanning
                    if (scanResult.IsSuccess && scanResult.Value is Modules.Startup.Models.StartupScanData startupData)
                    {
                        ContentTitle = current.Module.Info.Name;
                        ContentDescription = current.Module.Info.Description;
                        CurrentContent = new StartupViewModel(startupData, _pendingChangesService, _autorunEnrichment);
                    }
                    else
                    {
                        CurrentContent = null;
                        SetStatus(scanResult.ErrorMessage ?? "Failed to scan startup entries", StatusSeverity.Error);
                    }
                });
            }
            else if (current?.Module is Modules.Power.PowerModule)
            {
                var scanResult = await current.Module.ScanSystemStateAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (epoch != _contentEpoch)
                        return; // superseded by Home/Set Loader/newer navigation while scanning
                    if (scanResult.IsSuccess && scanResult.Value is Modules.Power.Models.PowerScanData powerData)
                    {
                        ContentTitle = current.Module.Info.Name;
                        ContentDescription = current.Module.Info.Description;
                        CurrentContent = new PowerViewModel(
                            powerData, _pendingChangesService, _powerService, _registryService,
                            _pendingActionsService);
                    }
                    else
                    {
                        CurrentContent = null;
                        SetStatus(scanResult.ErrorMessage ?? "Failed to scan power plans", StatusSeverity.Error);
                    }
                });
            }
            else if (current?.Module is Modules.Display.DisplayModule displayModule)
            {
                var scanResult = await current.Module.ScanSystemStateAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (epoch != _contentEpoch)
                        return; // superseded by Home/Set Loader/newer navigation while scanning
                    if (scanResult.IsSuccess
                        && scanResult.Value is Modules.Display.Models.DisplayScanData displayData
                        && _monitorService is not null && _powerService is not null)
                    {
                        ContentTitle = current.Module.Info.Name;
                        ContentDescription = current.Module.Info.Description;
                        // Snapshot or quick scan on screen now; the full scan
                        // runs behind it and fills the cards in.
                        CurrentContent = new DisplayViewModel(
                            displayData, _monitorService, _powerService, displayModule.RefreshAsync);
                    }
                    else
                    {
                        CurrentContent = null;
                        SetStatus(scanResult.ErrorMessage ?? "Failed to scan displays", StatusSeverity.Error);
                    }
                });
            }
            else if (current?.Module is Modules.Hardware.HardwareCompanionModule hardwareModule)
            {
                var scanResult = await current.Module.ScanSystemStateAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (epoch != _contentEpoch)
                        return; // superseded by Home/Set Loader/newer navigation while scanning
                    if (scanResult.IsSuccess
                        && scanResult.Value is Modules.Hardware.Models.HardwareTabScanData hardwareData)
                    {
                        ContentTitle = current.Module.Info.Name;
                        ContentDescription = current.Module.Info.Description;
                        // A cached snapshot is on screen now; when it is old
                        // enough, a fresh pass runs behind it and updates the page.
                        CurrentContent = new HardwareTabViewModel(
                            hardwareData,
                            _hardwareActions,
                            hardwareModule.RefreshAsync,
                            installAvailable: LookupModuleAvailability(Modules.Software.SoftwareModule.ModuleName)?.IsAvailable ?? false,
                            refreshOnOpen: hardwareData.RefreshInBackground,
                            lightingBackend: _lightingBackend,
                            cooling: hardwareModule is Modules.Hardware.CoolingModule coolingModule
                                ? new CoolingProfilesViewModel(coolingModule.Profiles, _pendingChangesService) : null);
                    }
                    else
                    {
                        CurrentContent = null;
                        SetStatus(scanResult.ErrorMessage ?? "Failed to check this PC's hardware", StatusSeverity.Error);
                    }
                });
            }
            else if (current?.Module is Modules.Software.SoftwareModule)
            {
                var scanResult = await current.Module.ScanSystemStateAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (epoch != _contentEpoch)
                        return; // superseded by Home/Set Loader/newer navigation while scanning
                    if (scanResult.IsSuccess
                        && scanResult.Value is Modules.Software.Models.SoftwareScanData softwareData
                        && _pendingActionsService is not null)
                    {
                        ContentTitle = current.Module.Info.Name;
                        ContentDescription = current.Module.Info.Description;
                        CurrentContent = new SoftwareViewModel(softwareData, _pendingActionsService, _wingetService);
                    }
                    else
                    {
                        CurrentContent = null;
                        SetStatus(scanResult.ErrorMessage ?? "Failed to load the app catalog", StatusSeverity.Error);
                    }
                });
            }
            else if (current?.Module is EnvironmentModule)
            {
                var scanResult = await current.Module.ScanSystemStateAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (epoch != _contentEpoch)
                        return; // superseded by Home/Set Loader/newer navigation while scanning
                    if (scanResult.IsSuccess && scanResult.Value is Modules.Shell.Models.EnvironmentScanData envData)
                    {
                        ContentTitle = current.Module.Info.Name;
                        ContentDescription = current.Module.Info.Description;
                        CurrentContent = new EnvironmentViewModel(envData, _pendingChangesService);
                    }
                    else
                    {
                        CurrentContent = null;
                        SetStatus(scanResult.ErrorMessage ?? "Failed to scan environment variables", StatusSeverity.Error);
                    }
                });
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (current is not null)
                    {
                        ContentTitle = current.Module.Info.Name;
                        ContentDescription = current.Module.Info.Description;
                    }
                    CurrentContent = null;
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Module load failed");
            await Dispatcher.UIThread.InvokeAsync(() =>
                SetStatus($"Failed to load module: {ex.Message}", StatusSeverity.Error));
        }
        finally
        {
            var completedEpoch = epoch;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // A newer navigation owns the overlay now; leave its state alone.
                if (completedEpoch == _contentEpoch)
                    IsModuleLoading = false;
            });
        }
    }

    [ObservableProperty]
    private bool _isModuleLoading;

    [ObservableProperty]
    private string _loadingText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UiScale))]
    private int _zoomPercent = 100;

    public double UiScale => ZoomPercent / 100.0;

    /// <summary>Changes the app zoom by ten percentage points, or resets it when direction is zero.</summary>
    public void ChangeZoom(int direction)
    {
        ZoomPercent = direction == 0 ? 100 : Math.Clamp(ZoomPercent + Math.Sign(direction) * 10, 50, 150);
        _settingsService?.SetApp(Core.Settings.AppSettingKeys.UiZoom,
            ZoomPercent.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Log.Info("Zoom: {Percent}%", ZoomPercent);
        ShowZoomOverlay();
    }

    // --- Zoom overlay: a pill over the page for a moment after each zoom change.
    // It floats over the content, so it takes no layout space (the status line
    // used to gain a row for it). Each change restarts the hide delay; a stale
    // timer from an earlier change is recognised by its generation and ignored.

    private static readonly TimeSpan DefaultZoomOverlayLifetime = TimeSpan.FromSeconds(2);
    private int _zoomOverlayGeneration;

    /// <summary>How long the pill stays; TimeSpan.Zero keeps it up (screenshot suites).</summary>
    public TimeSpan ZoomOverlayLifetime { get; set; } = DefaultZoomOverlayLifetime;

    [ObservableProperty]
    private bool _isZoomOverlayVisible;

    [ObservableProperty]
    private string _zoomOverlayText = string.Empty;

    private void ShowZoomOverlay()
    {
        ZoomOverlayText = $"Zoom {ZoomPercent}%";
        IsZoomOverlayVisible = true;
        var generation = ++_zoomOverlayGeneration;
        if (ZoomOverlayLifetime <= TimeSpan.Zero)
            return;

        DispatcherTimer.RunOnce(() => HideZoomOverlay(generation), ZoomOverlayLifetime);
    }

    /// <summary>Hides the pill only when no newer zoom change has restarted the delay.</summary>
    public void HideZoomOverlay(int generation)
    {
        if (generation == _zoomOverlayGeneration)
            IsZoomOverlayVisible = false;
    }

    /// <summary>The generation the next hide must match; tests use it to replay the timer by hand.</summary>
    public int ZoomOverlayGeneration => _zoomOverlayGeneration;

    public async Task InitializeAsync()
    {
        await _changeHistoryService.InitializeAsync().ConfigureAwait(true);
        await _navigationService.InitializeAsync().ConfigureAwait(true);

        PopulateSidebar();

        // Home is the launch default (10.5): a cheap read-only dashboard;
        // no module scan runs until the user navigates to one.
        OpenHome();

        if (_settingsService?.SettingsWereReset == true)
            SetStatus("Settings were reset to defaults (the previous file was corrupt; it was preserved as settings.json.bad)", StatusSeverity.Warning);
        else if (_settingsService?.LoadError is not null)
            SetStatus("The settings file could not be read - changes to settings will not be saved this session", StatusSeverity.Warning);

        // 7-3: fire-and-forget update check; never blocks startup, never surfaces
        // failures (fully offline-safe). Skipped entirely when the user opted out.
        _ = CheckForUpdateBadgeAsync();

        // The Display page's full DDC scan takes seconds; do it now, in the
        // background, so the first open is instant.
        _ = PrewarmDisplaySnapshotAsync();
    }

    private void PopulateSidebar()
    {
        SidebarGroups.Clear();

        var groups = _navigationService.Modules
            .GroupBy(m => m.Module.Info.Group)
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            var groupVm = new SidebarGroupViewModel
            {
                GroupName = group.Key.ToString().ToUpperInvariant()
            };

            foreach (var registration in group.OrderBy(m => m.Module.Info.LoadOrder))
            {
                groupVm.Items.Add(new SidebarItemViewModel
                {
                    Name = registration.Module.Info.Name,
                    Icon = registration.Module.Info.Icon,
                    UnavailableReason = registration.Availability.Reason,
                    RemediationHint = registration.Availability.RemediationHint,
                    IsAvailable = registration.Availability.IsAvailable,
                    Module = registration.Module,
                });
            }

            SidebarGroups.Add(groupVm);
        }
    }

    // --- 5-3 cross-module search ---

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _hasSearchResults;

    /// <summary>
    /// The results dropdown under the title bar search. Opens whenever a query
    /// has results; the window closes it on a click outside or Escape without
    /// touching the query, so the next keystroke reopens it.
    /// </summary>
    [ObservableProperty]
    private bool _isSearchOpen;

    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = [];

    partial void OnSearchQueryChanged(string value)
    {
        _searchService ??= new Core.Search.SettingsSearchService(
            _searchContributors,
            moduleId =>
            {
                var registration = _navigationService.Modules
                    .FirstOrDefault(m => m.Module.Info.Name == moduleId);
                return registration is null
                    ? (false, "Module not installed")
                    : (registration.Availability.IsAvailable, registration.Availability.Reason);
            });

        SearchResults.Clear();
        foreach (var result in _searchService.Search(value))
            SearchResults.Add(new SearchResultViewModel(result));
        HasSearchResults = SearchResults.Count > 0;
        IsSearchOpen = HasSearchResults;
    }

    // Set when navigation came from a search result; the arriving content VM
    // consumes it (5-3: the matching card should be what the user lands on).
    // Stamped with the content epoch of that navigation so a superseding
    // navigation (Home mid-scan, failed scan then elsewhere) drops the focus
    // instead of injecting it into an unrelated page.
    private SearchResultViewModel? _pendingSearchResult;
    private int _pendingSearchFocusEpoch;

    [RelayCommand]
    private void SelectSearchResult(SearchResultViewModel? result)
    {
        if (result is null || !result.IsAvailable)
            return;

        SearchQuery = string.Empty;
        _pendingSearchResult = result;
        _pendingSearchFocusEpoch = _contentEpoch;
        var epochBefore = _contentEpoch;
        NavigateToModuleByName(result.ModuleId);
        // Any rebuild bumps the epoch synchronously before this line. An
        // unchanged epoch means no rebuild is coming (the target module is the
        // page already on screen), so the live page consumes the focus here.
        _pendingSearchFocusEpoch = _contentEpoch;
        if (_contentEpoch == epochBefore && _pendingSearchResult is not null && CurrentContent is not null)
            ApplySearchFocus(CurrentContent);
    }

    partial void OnCurrentContentChanged(object? value)
    {
        if (_refreshTabState is { } refresh)
        {
            if (refresh.Epoch != _contentEpoch)
                _refreshTabState = null;
            else if (value is not null)
            {
                _refreshTabState = null;
                if (value.GetType() == refresh.PageType && value is ITabbedPage page)
                {
                    page.SelectedTabIndex = refresh.TabIndex;
                    if (value is PowerViewModel power && refresh.PlanId is { } planId)
                        _ = power.RestoreSettingsAfterRefreshAsync(planId, refresh.GroupId);
                }
            }
        }
        if (_pendingSearchResult is null)
            return;
        if (_contentEpoch != _pendingSearchFocusEpoch)
        {
            // A different navigation owns the content now; the moment has passed.
            _pendingSearchResult = null;
            return;
        }
        // Content flips to null while the module scans; only real content consumes.
        if (value is not null)
            ApplySearchFocus(value);
    }

    private void ApplySearchFocus(object content)
    {
        var result = _pendingSearchResult!;
        var focus = result.Name;
        _pendingSearchResult = null;
        if (content is ISearchNavigationTarget navigationTarget)
            navigationTarget.NavigateToSearchResult(result.SettingId, result.Name);
        else if (content is ISearchFocusTarget target)
            target.SearchText = focus;
        else
            SetStatus($"Look for \"{focus}\" on this page", StatusSeverity.Success);
    }

    private const string FirstLaunchDismissedKey = "firstLaunchBannerDismissed";

    /// <summary>Null when already dismissed or the settings/detector plumbing is absent.</summary>
    private FirstLaunchBannerViewModel? BuildFirstLaunchBanner()
    {
        if (_settingsService is null || _capabilityDetector is null)
            return null;
        if (_settingsService.GetAppBool(FirstLaunchDismissedKey, fallback: false))
            return null;

        var moduleRows = _navigationService.Modules
            .Select(m => new FirstLaunchRowViewModel(
                m.Module.Info.Name,
                m.Availability.IsAvailable
                    ? m.Module.Info.Description
                    : $"{m.Availability.Reason} {m.Availability.RemediationHint}".Trim(),
                m.Availability.IsAvailable,
                m.Availability.IsAvailable
                    ? () => NavigateToModuleByName(m.Module.Info.Name)
                    : null))
            .ToList();

        // Hardware ecosystem rows only; the always-present subsystems say nothing useful.
        var capabilityRows = _capabilityDetector.GetCapabilityReport()
            .Where(r => r.Capability is Core.Modules.SystemCapability.DdcCi
                or Core.Modules.SystemCapability.HwInfo
                or Core.Modules.SystemCapability.AsusAtkacpi
                or Core.Modules.SystemCapability.OpenRgb)
            .Select(r => new FirstLaunchRowViewModel(
                r.DisplayName,
                r.Availability.IsAvailable
                    ? $"Detected. {r.Availability.RemediationHint}".Trim()
                    : $"{r.Availability.Reason} {r.Availability.RemediationHint}".Trim(),
                r.Availability.IsAvailable))
            .ToList();

        var banner = new FirstLaunchBannerViewModel(moduleRows, capabilityRows);
        banner.Dismissed += (_, _) => MarkFirstLaunchBannerDismissed();
        _firstLaunchBannerActive = true;
        return banner;
    }

    private bool _firstLaunchBannerActive;

    private void MarkFirstLaunchBannerDismissed()
    {
        _firstLaunchBannerActive = false;
        _settingsService?.SetApp(FirstLaunchDismissedKey, "1");
    }

    /// <summary>
    /// The title row's refresh: rebuilds whatever page is showing. A module
    /// page rescans (Display drops its snapshot first, so the rescan is real);
    /// Home, Settings, Presets, and the Gallery rebuild the same way their
    /// sidebar entries open them.
    /// </summary>
    private sealed record RefreshTabState(int Epoch, Type PageType, int TabIndex, Guid? PlanId, Guid? GroupId);
    private RefreshTabState? _refreshTabState;

    [RelayCommand]
    private void RefreshPage()
    {
        if (IsModuleLoading) return;
        _refreshTabState = CurrentContent is ITabbedPage page
            ? new RefreshTabState(_contentEpoch + 1, CurrentContent.GetType(), page.SelectedTabIndex,
                (CurrentContent as PowerViewModel)?.SettingsPlan?.Plan.PlanGuid,
                (CurrentContent as PowerViewModel)?.SelectedSettingsGroupId)
            : null;
        if (IsHomeActive) { OpenHome(); return; }
        if (IsSettingsActive) { OpenSettings(); return; }
        if (IsSetLoaderActive) { OpenSetLoader(); return; }
        if (IsDebugActive) { OpenDebug(); return; }
        if (_navigationService.CurrentModule is not { } current)
            return;

        (current.Module as Modules.Display.DisplayModule)?.InvalidateSnapshot();
        (current.Module as Modules.Hardware.HardwareCompanionModule)?.InvalidateSnapshot();
        OnNavigationPropertyChanged(
            _navigationService,
            new PropertyChangedEventArgs(nameof(NavigationService.CurrentModule)));
    }

    private void NavigateToModuleByName(string moduleName)
    {
        var item = SidebarGroups.SelectMany(g => g.Items)
            .FirstOrDefault(i => i.Name == moduleName);
        if (item is not null)
            NavigateToModule(item);
    }

    [RelayCommand]
    private void NavigateToModule(SidebarItemViewModel? item)
    {
        if (item is null || !item.IsAvailable)
            return;

        // 5-2: navigating anywhere counts as having seen the first-launch summary
        if (_firstLaunchBannerActive)
            MarkFirstLaunchBannerDismissed();

        var wasSetLoaderActive = IsSetLoaderActive;
        var wasHomeActive = IsHomeActive;
        var wasSettingsActive = IsSettingsActive;
        // Captured BEFORE navigating: afterwards CurrentModule always equals the target,
        // which would double-trigger the rebuild for cross-module navigation (the
        // PropertyChanged event already fired for that case).
        var previousModule = _navigationService.CurrentModule?.Module;
        (CurrentContent as SetLoaderViewModel)?.Dispose();
        IsDebugActive = false;
        IsSetLoaderActive = false;
        IsHomeActive = false;
        IsSettingsActive = false;
        _navigationService.NavigateToModule(item.Name);
        SyncSelectedModule();

        // Returning from the Set Loader, Home, or Settings to the module that is still
        // CurrentModule: the navigation setter guards equality, so rebuild explicitly.
        if ((wasSetLoaderActive || wasHomeActive || wasSettingsActive) && previousModule == item.Module)
        {
            OnNavigationPropertyChanged(
                _navigationService,
                new PropertyChangedEventArgs(nameof(NavigationService.CurrentModule)));
        }
    }

    [ObservableProperty]
    private bool _isSetLoaderActive;

    [RelayCommand]
    private void OpenSetLoader()
    {
        _contentEpoch++;
        IsModuleLoading = false;
        // Fresh disk read on every open: user sets dropped into %ProgramData% appear
        // without an app restart. Outgoing content may also be a module VM reached
        // without a navigation event; its subscriptions must not outlive the switch.
        (CurrentContent as IDisposable)?.Dispose();
        var loadResult = _setProvider.LoadSets();

        ContentTitle = "Presets";
        ContentDescription = "Browse curated tweak presets and preview every change before applying";
        CurrentContent = new SetLoaderViewModel(
            loadResult, _setEntryInspectors, LookupModuleAvailability, _pendingChangesService,
            _capabilityDetector);
        IsSetLoaderActive = true;
        IsHomeActive = false;
        IsSettingsActive = false;
        SelectedModule = null;

        ClearSidebarActives();
    }

    [RelayCommand]
    private void OpenRepository() => OpenUrl(Core.AppConstants.RepositoryUrl, "the GitHub page");

    [RelayCommand]
    private void OpenBugReport() => OpenUrl(Core.AppConstants.BugReportUrl, "the bug report form");

    private void OpenUrl(string url, string what)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus($"Could not open {what}: {url}", StatusSeverity.Warning);
        }
    }

    // --- Title bar identity and About ---

    /// <summary>Three-part assembly version, e.g. 0.1.0.</summary>
    public static string AppVersion { get; } =
        typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public string VersionText => $"Version {AppVersion}";

    public string PublisherText => Core.AppConstants.PublisherName;

    /// <summary>The About card under the title bar; the window closes it on a click outside or Escape.</summary>
    [ObservableProperty]
    private bool _isAboutOpen;

    [RelayCommand]
    private void ToggleAbout() => IsAboutOpen = !IsAboutOpen;

    [ObservableProperty]
    private bool _isSettingsActive;

    [RelayCommand]
    private void OpenSettings()
    {
        if (_settingsService is null)
            return;

        _contentEpoch++;
        IsModuleLoading = false;
        // Old content may be a module VM or the Set Loader; subscriptions must not
        // outlive the switch.
        (CurrentContent as IDisposable)?.Dispose();

        ContentTitle = "Settings";
        ContentDescription = "Application preferences - every change saves immediately";
        CurrentContent = new SettingsViewModel(
            _settingsService,
            _moduleSettingsContributors,
            applyTheme: Services.ThemeService.Apply,
            installedModuleIds: _navigationService.Modules.Select(m => m.Module.Info.Name).ToList(),
            appVersion: AppVersion,
            capabilityReport: _capabilityDetector?.GetCapabilityReport(),
            ownerMode: _ownerModeControl is { } ownerMode ? new OwnerModeSectionViewModel(ownerMode) : null);
        IsSettingsActive = true;
        IsDebugActive = false;
        IsSetLoaderActive = false;
        IsHomeActive = false;
        SelectedModule = null;
        ClearSidebarActives();
    }

    /// <summary>
    /// After resume or a display change: re-push this session's monitor writes
    /// (monitors forget DDC state across sleep), then quietly refresh the
    /// Display page if it is open so stale monitors never linger.
    /// </summary>
    public async Task HandleDisplayTopologyChangedAsync()
    {
        if (_monitorService is null)
            return;

        await Task.Run(() => _monitorService.ReapplyLastWrites()).ConfigureAwait(true);

        // Monitors may have come or gone: the snapshot is stale either way.
        var displayModule = _navigationService.Modules
            .Select(m => m.Module)
            .OfType<Modules.Display.DisplayModule>()
            .FirstOrDefault();
        displayModule?.InvalidateSnapshot();

        if (CurrentContent is not DisplayViewModel
            || SelectedModule?.Module is not Modules.Display.DisplayModule module
            || _powerService is null)
        {
            return;
        }

        var epoch = _contentEpoch;
        var scan = await module.ScanSystemStateAsync().ConfigureAwait(true);
        if (epoch != _contentEpoch)
            return; // the user navigated away meanwhile

        if (scan.IsSuccess && scan.Value is Modules.Display.Models.DisplayScanData data)
            CurrentContent = new DisplayViewModel(data, _monitorService, _powerService, module.RefreshAsync);
    }

    /// <summary>
    /// Warms the Display snapshot shortly after startup, so the first click on
    /// Display finds it ready. Delayed past the Home page's own reads, and
    /// skipped when the module is not installed or not available.
    /// </summary>
    private async Task PrewarmDisplaySnapshotAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            var registration = _navigationService.Modules
                .FirstOrDefault(m => m.Module is Modules.Display.DisplayModule);
            if (registration is not { Availability.IsAvailable: true }
                || registration.Module is not Modules.Display.DisplayModule module
                || module.Snapshot is not null)
            {
                return;
            }

            await module.RefreshAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A warm-up that fails changes nothing; the page scans on open as before.
        catch (Exception ex)
        {
            Log.Warn(ex, "Display snapshot warm-up failed");
        }
#pragma warning restore CA1031
    }

    [ObservableProperty]
    private bool _isDebugActive;

    /// <summary>The Debug page (gallery, test controls, state simulation) is dev-facing; Release builds hide it.</summary>
#if DEBUG
    public static bool IsDebugVisible => true;
#else
    public static bool IsDebugVisible => false;
#endif

    /// <summary>Dev-facing Debug page; Debug builds only.</summary>
    [RelayCommand]
    private void OpenDebug()
    {
        _contentEpoch++;
        IsModuleLoading = false;
        (CurrentContent as IDisposable)?.Dispose();

        ContentTitle = DebugViewModel.Title;
        ContentDescription = "Style reference, test controls, and state simulation for development";
        CurrentContent = new DebugViewModel(
            _debugSimulation ?? new Services.DebugSimulation(),
            _explorerRestartService,
            _notificationService,
            showToast: (title, message, severity) => ToastStack.Show(title, message, severity),
            showRestartBanner: requirement => ShowRestartNotice([requirement]),
            stageSampleChange: StageDebugChange);
        IsDebugActive = true;
        IsSettingsActive = false;
        IsSetLoaderActive = false;
        IsHomeActive = false;
        SelectedModule = null;
        ClearSidebarActives();
    }

    // --- Debug simulation: what the app believes about the PC, overridden in memory.

    private readonly Services.DebugSimulation? _debugSimulation;

    // The Owner Mode control every consumer sees: the real service, or the
    // simulated wrapper over it when a simulation seam is registered. Cards, the
    // Settings service card, and the capability probe all read through it.
    private readonly Services.IOwnerModeServiceControl? _ownerModeControl;

    /// <summary>True while any simulation override is set; the banner and the Apply guard read it.</summary>
    [ObservableProperty]
    private bool _isSimulationActive;

    [ObservableProperty]
    private string _simulationBannerText = string.Empty;

    private void OnSimulationChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            ApplySimulationChange();
        else
            Dispatcher.UIThread.Post(ApplySimulationChange);
    }

    private void ApplySimulationChange()
    {
        var active = _debugSimulation?.IsActive == true;
        IsSimulationActive = active;
        SimulationBannerText = active
            ? $"Simulated mode: {_debugSimulation!.Summary}. Apply, installs, and service actions are off."
            : string.Empty;
        Log.Info("Simulation: {State}", active ? _debugSimulation!.Summary : "off");

        // Pages read the detector when they are built; rebuild the open one so its
        // cards and callouts show the simulated state. The Debug page itself keeps
        // its dropdowns (it is the page being edited).
        if (CurrentContent is not DebugViewModel && !IsModuleLoading)
            RefreshPage();
    }

    [RelayCommand]
    private void ResetSimulation() => _debugSimulation?.Reset();

    /// <summary>
    /// Every real mutation starts here: apply, undo, redo, Explorer restart. The
    /// lease refuses while simulation is active and locks the simulation out
    /// until disposed. With no simulation seam it is always open.
    /// </summary>
    private bool _shutdownRequested;

    private Services.MutationLease BeginMutation() => _shutdownRequested
        ? Services.MutationLease.Refused("The app is closing. New changes cannot start.")
        : _debugSimulation?.BeginMutation() ?? Services.MutationLease.Open();

    /// <summary>Cancels pending acquisition and waits for active reversible operations before service disposal.</summary>
    public async Task PrepareForShutdownAsync()
    {
        _shutdownRequested = true;
        _deliberateChanges?.StopAcceptingChanges();
        ApplyAllCommand.Cancel();
        var tasks = new[] { ApplyAllCommand.ExecutionTask, ChangeHistory.RestoreCommand.ExecutionTask, ChangeHistory.RedoCommand.ExecutionTask };
        await Task.WhenAll(tasks.OfType<Task>()).ConfigureAwait(true);
    }

    /// <summary>
    /// False while a staged group is unresolved: the page's controls are
    /// disabled and nothing new is staged, because a card would stage from the
    /// values it scanned before the failed apply. Discard All clears it and
    /// reloads the page.
    /// </summary>
    public bool IsContentInteractive => !HasUnresolvedGroups;

    /// <summary>The line every refused staging shows.</summary>
    public const string StagingBlockedMessage =
        "A change from an earlier apply is in an unknown state. Click Discard All in the review panel first; the page reloads, then set changes again.";

    private bool RefuseStagingWhileUnresolved()
    {
        if (!HasUnresolvedGroups)
            return false;
        SetStatus(StagingBlockedMessage, StatusSeverity.Error);
        return true;
    }

    partial void OnHasUnresolvedGroupsChanged(bool value) => OnPropertyChanged(nameof(IsContentInteractive));

    private int _debugChangeCounter;

    /// <summary>
    /// Stages a sample change against a module that does not exist, so the
    /// apply bar, badge, and review panel can be exercised and an Apply fails
    /// harmlessly. F5 to F7 and the Debug page both come here.
    /// </summary>
    public void StageDebugChange(ChangeCategory category)
    {
        if (RefuseStagingWhileUnresolved())
            return;
        _debugChangeCounter++;
        var enable = category == ChangeCategory.Enable;
        var disable = category == ChangeCategory.Disable;
        _pendingChangesService.Stage(new ChangeDescriptor
        {
            ModuleId = "DebugModule",
            SettingId = $"debug-setting-{_debugChangeCounter}",
            DisplayName = $"Test Setting {_debugChangeCounter}",
            SystemLocation = @$"HKLM\SOFTWARE\Debug\Setting{_debugChangeCounter}",
            BeforeValue = "0",
            AfterValue = "1",
            BeforeDisplay = enable ? "Disabled" : disable ? "Enabled" : "Value A",
            AfterDisplay = enable ? "Enabled" : disable ? "Disabled" : "Value B",
            ValueType = ChangeValueType.Registry_DWord,
            Category = category,
        });
    }

    [ObservableProperty]
    private bool _isHomeActive;

    [RelayCommand]
    private void OpenHome()
    {
        _contentEpoch++;
        IsModuleLoading = false;
        // Old content may be a module VM (reached without a navigation event) or the
        // Set Loader; either way its subscriptions must not outlive the switch.
        (CurrentContent as IDisposable)?.Dispose();

        ContentTitle = "Home";
        ContentDescription = "System overview and recent activity";

        var identity = new SystemIdentityService(
            _registryService,
            new Interop.Win32.InstalledMemoryProvider(),
            new Interop.Win32.Display.GpuIdentityProvider()).Read();
        if (_debugSimulation is { IsActive: true } simulation)
        {
            // Presentation only: the identity card says what is being simulated.
            if (simulation.Device is { } device)
            {
                identity = identity with
                {
                    Manufacturer = device.Manufacturer,
                    Model = device.Model,
                    SystemType = device.SystemType,
                };
            }
            if (simulation.Sku is { } sku)
                identity = identity with { WindowsEdition = $"Windows 11 {Services.DebugSimulation.SkuName(sku)} (simulated)" };
        }

        var home = new HomeViewModel(
            identity,
            _changeHistoryService,
            BuildFirstLaunchBanner(),
            BuildMonitoringSection(),
            _driftSection,
            _debugSimulation is { IsActive: true } ? null : _hardwareDetection);
        CurrentContent = home;
        IsHomeActive = true;
        IsDebugActive = false;
        IsSetLoaderActive = false;
        IsSettingsActive = false;
        SelectedModule = null;
        ClearSidebarActives();

        // Recent activity fills in asynchronously; the dashboard never blocks.
        _ = home.LoadRecentActivityCommand.ExecuteAsync(null);
        _ = home.LoadHardwareAsync();
    }

    private readonly Core.Hardware.IHardwareDetectionService? _hardwareDetection;

    private void ClearSidebarActives()
    {
        foreach (var group in SidebarGroups)
        {
            foreach (var sidebarItem in group.Items)
                sidebarItem.IsActive = false;
        }
    }

    private ModuleAvailability? LookupModuleAvailability(string moduleId)
        => _navigationService.Modules
            .FirstOrDefault(m => m.Module.Info.Name == moduleId)?.Availability;

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
    }

    [ObservableProperty]
    private bool _isReviewPanelOpen;

    [RelayCommand]
    private void OpenReviewPanel()
    {
        IsReviewPanelOpen = true;
    }

    [RelayCommand]
    private void CloseReviewPanel()
    {
        IsReviewPanelOpen = false;
    }

    [RelayCommand]
    private void OpenHistoryPanel()
    {
        IsHistoryPanelOpen = true;
        ChangeHistory.LoadHistoryCommand.Execute(null);
    }

    [RelayCommand]
    private void CloseHistoryPanel()
    {
        IsHistoryPanelOpen = false;
    }

    [RelayCommand]
    private async Task RestartExplorerAsync()
    {
        if (IsRestartingExplorer)
            return;

        using var lease = BeginMutation();
        if (lease.Refusal is { } refusal)
        {
            SetStatus(refusal, StatusSeverity.Error);
            return;
        }

        IsRestartingExplorer = true;
        SetStatus("Restarting Explorer...", StatusSeverity.Warning);

        try
        {
            var result = await _explorerRestartService.RestartExplorerAsync().ConfigureAwait(true);

            if (result.IsSuccess)
            {
                IsRestartNotificationVisible = false;
                IsRestartActionAvailable = false;
                SetStatus("Explorer restarted successfully", StatusSeverity.Success);
            }
            else
            {
                SetStatus($"Failed to restart Explorer: {result.ErrorMessage}", StatusSeverity.Error);
            }
        }
        finally
        {
            IsRestartingExplorer = false;
        }
    }

    [RelayCommand]
    private void DismissRestartNotification()
    {
        IsRestartNotificationVisible = false;
    }

    /// <summary>
    /// Drops everything staged. Discarding does not touch the PC: a change that
    /// was left in an unknown state stays however Windows left it. What discard
    /// does is clear the block on the queue and, when the open page belongs to a
    /// module with such a change, scan that module again so its cards show what
    /// Windows has now and stage from that, never from the values read earlier.
    /// </summary>
    [RelayCommand]
    private async Task DiscardAllAsync()
    {
        foreach (var record in _pendingChangesService.ReconciliationRequired)
        {
            foreach (var change in record.Uncertain)
                _modulesNeedingRescan.Add(change.ModuleId);
        }

        _pendingChangesService.DiscardAll();
        _pendingActionsService?.DiscardAll();
        _applyWithoutRestorePoint = false;
        IsReviewPanelOpen = false;
        // The block is lifted with the queue; a refusal or failure line from before
        // the discard would now describe a state that no longer exists.
        if (StatusSeverity == StatusSeverity.Error)
            SetStatus(string.Empty, StatusSeverity.Success);

        if (_modulesNeedingRescan.Count == 0)
            return;

        var current = _navigationService.CurrentModule?.Module.Info.Name;
        var rescanCurrent = current is not null
            && SelectedModule is not null
            && _modulesNeedingRescan.Contains(current);

        // Pages are rebuilt from a fresh scan on every navigation, so a module
        // that is not on screen has no stale cards to reset. Only the open page
        // needs an explicit reload.
        _modulesNeedingRescan.Clear();
        if (!rescanCurrent)
            return;

        SetStatus($"Reading current {current} settings...", StatusSeverity.Warning);
        await LoadCurrentModuleAsync().ConfigureAwait(true);
        if (CurrentContent is not null)
            SetStatus($"{current} settings reloaded. Set the change again if you still want it.", StatusSeverity.Success);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateRestorePoint))]
    private bool _isCreatingRestorePoint;

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        if (IsCreatingRestorePoint || IsApplying)
            return;

        IsCreatingRestorePoint = true;
        SetStatus("Creating restore point...", StatusSeverity.Warning);
        try
        {
            var description = $"ThisIsMyPC restore point {DateTime.Now:yyyy-MM-dd HH:mm}";
            var result = _privilegeBroker is null
                ? await _restorePointService.CreateRestorePointAsync(description).ConfigureAwait(true)
                : await CreateBrokerRestorePoint(description).ConfigureAwait(true);

            if (result.IsSuccess)
            {
                _applyWithoutRestorePoint = false;
                SetStatus("Restore point created successfully", StatusSeverity.Success);
            }
            else
            {
                SetStatus(result.Message ?? "Restore point creation failed", StatusSeverity.Error);
            }
        }
        finally
        {
            IsCreatingRestorePoint = false;
        }
    }

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // The view colors the status text through style classes with DynamicResource
    // setters, so a live theme switch restyles it; a snapshotted IBrush would not.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusSuccess))]
    [NotifyPropertyChangedFor(nameof(IsStatusWarning))]
    [NotifyPropertyChangedFor(nameof(IsStatusError))]
    private StatusSeverity _statusSeverity = StatusSeverity.Success;

    public bool IsStatusSuccess => StatusSeverity == StatusSeverity.Success;
    public bool IsStatusWarning => StatusSeverity == StatusSeverity.Warning;
    public bool IsStatusError => StatusSeverity == StatusSeverity.Error;

    /// <summary>Every status line is also a log line, so an error on screen can be copied from the log.</summary>
    private void SetStatus(string message, StatusSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
        if (string.IsNullOrEmpty(message))
            return;
        switch (severity)
        {
            case StatusSeverity.Error:
                Log.Error("Status: {Message}", message);
                break;
            case StatusSeverity.Warning:
                Log.Warn("Status: {Message}", message);
                break;
            default:
                Log.Info("Status: {Message}", message);
                break;
        }
    }

    [RelayCommand]
    private async Task ApplyAllAsync(CancellationToken cancellationToken)
    {
        if (!HasPendingChanges || IsApplying || IsCreatingRestorePoint)
            return;

        // A simulated edition or capability must never authorize a real write,
        // and the lease keeps the simulation from switching on while the batch
        // (restore point, every module call, rollback, one-way actions) runs.
        using var lease = BeginMutation();
        if (lease.Refusal is { } refusal)
        {
            SetStatus(refusal, StatusSeverity.Error);
            return;
        }

        IsApplying = true;
        StatusMessage = string.Empty;

        try
        {
            // FR64: safety net before bulk batches. Counts individual descriptors;
            // PendingCount counts groups, so a single 6-change set must still trigger.
            // One-way actions count too: a bulk of Appx removals is the least
            // reversible thing in the app and deserves the restore point most.
            var brokerChanges = _pendingChangesService.PendingGroups.SelectMany(group => group.Changes)
                .Where(change => !IsLocalCoolingProfile(change)).ToList();
            var changeCount = brokerChanges.Count
                + (_pendingActionsService?.PendingCount ?? 0);
            var restorePointDescription = changeCount >= AutoRestorePointThreshold && !_applyWithoutRestorePoint
                ? $"ThisIsMyPC: Before applying {changeCount} changes"
                : null;

            IPrivilegeBrokerSession? brokerSession = null;
            if (_privilegeBroker is not null && (brokerChanges.Count > 0
                || (_pendingActionsService?.PendingCount ?? 0) > 0 || restorePointDescription is not null))
            {
                var opened = await _privilegeBroker.OpenSessionAsync(new Ipc.Contracts.BrokerSessionRequest
                {
                    Changes = brokerChanges,
                    Actions = _pendingActionsService?.PendingActions ?? [],
                    RestorePointDescription = restorePointDescription,
                }, cancellationToken).ConfigureAwait(true);
                if (!opened.IsSuccess)
                {
                    SetStatus(opened.ErrorMessage ?? "Administrator confirmation failed.", StatusSeverity.Error);
                    return;
                }
                brokerSession = opened.Value!;
                _activeBrokerSession = brokerSession;
            }

            await using var brokerScope = brokerSession;
            if (changeCount >= AutoRestorePointThreshold && !_applyWithoutRestorePoint)
            {
                SetStatus("Creating restore point...", StatusSeverity.Warning);
                var restorePoint = brokerSession is null
                    ? await _restorePointService.CreateRestorePointAsync(restorePointDescription!).ConfigureAwait(true)
                    : await brokerSession.CreateRestorePointAsync(restorePointDescription!, cancellationToken).ConfigureAwait(true);

                if (!restorePoint.IsSuccess)
                {
                    _applyWithoutRestorePoint = true;
                    SetStatus(
                        $"{restorePoint.Message ?? "Restore point creation failed"}. Click Apply again to proceed without a restore point.",
                        StatusSeverity.Error);
                    return;
                }
            }
            else if (_applyWithoutRestorePoint)
            {
                SetStatus("Applying without a restore point", StatusSeverity.Warning);
            }

            Log.Info("Apply: {Groups} group(s), {Changes} change(s), {Actions} action(s)",
                _pendingChangesService.PendingGroups.Count,
                _pendingChangesService.PendingGroups.Sum(g => g.Changes.Count),
                _pendingActionsService?.PendingCount ?? 0);

            cancellationToken.ThrowIfCancellationRequested();
            // Each module callback enters through the dispatcher, including calls after lease waits or rollback awaits.
            var result = _deliberateChanges is null
                ? await _pendingChangesService.ApplyAllAsync(
                    ApplyChangeToModule, RevertChangeOnModule, cancellationToken).ConfigureAwait(true)
                : await _deliberateChanges.ApplyAsync(_pendingChangesService, _changeHistoryService,
                    change => Dispatcher.UIThread.InvokeAsync(() => ApplyChangeToModule(change)),
                    change => Dispatcher.UIThread.InvokeAsync(() => RevertChangeOnModule(change)),
                    dispatch: operation => Dispatcher.UIThread.InvokeAsync(operation), cancellationToken: cancellationToken).ConfigureAwait(true);

            if (result.IsSuccess)
            {
                Log.Info("Apply: {Count} change(s) applied; restarts needed: {Restarts}",
                    result.Applied.Count, string.Join(", ", result.RequiredRestarts));
            }
            else
            {
                Log.Error(result.Exception,
                    "Apply stopped ({Kind}) at {Module}/{Setting} ({Location}) [{Category}]: {Error}; {Applied} applied before it, {RolledBack} rolled back, {NotRolledBack} not rolled back, {Uncertain} uncertain",
                    result.FailureKind, result.Failed?.ModuleId, result.Failed?.SettingId, result.Failed?.SystemLocation,
                    result.ErrorCategory, result.ErrorMessage, result.Applied.Count, result.RolledBack.Count,
                    result.RollbackFailures.Count, result.Uncertain.Count);
                foreach (var uncertain in result.Uncertain)
                    Log.Warn("Apply: live value unknown for {Module}/{Setting} at {Location}", uncertain.ModuleId, uncertain.SettingId, uncertain.SystemLocation);
            }

            // Applied holds only changes from groups that finished and left the queue,
            // on every exit: success, failure, throw, or cancellation. Record it once,
            // here, so a group that completed before a later one failed still has its
            // undo entry. The failed change, rollback failures, and the uncertain list
            // are not on Applied and never reach history.
            if (_deliberateChanges is null && result.Applied.Count > 0)
                await _changeHistoryService.RecordChangesAsync(result).ConfigureAwait(true);

            // Cards on a page whose change ended in an unknown state read their
            // before values at load; those may be stale now. Remember the module so
            // Discard All scans it again before anything is staged from it.
            foreach (var uncertain in result.Uncertain)
                _modulesNeedingRescan.Add(uncertain.ModuleId);

            var restartNote = ShowRestartNotice(result.RequiredRestarts);

            if (result.IsSuccess)
            {
                _applyWithoutRestorePoint = false;
                IsReviewPanelOpen = false;
                SetStatus(
                    restartNote is null ? "Changes applied successfully" : $"Changes applied. {restartNote}",
                    restartNote is null || result.RequiredRestarts.Contains(RestartRequirement.ExplorerRefresh)
                        ? StatusSeverity.Success
                        : StatusSeverity.Warning);
            }
            else
            {
                var unresolvedGroups = _pendingChangesService.ReconciliationRequired
                    .Select(r => r.Group.DisplayName)
                    .ToList();
                var message = FormatApplyError(result, unresolvedGroups);
                if (restartNote is not null)
                    message += $" Completed changes: {restartNote.ToLowerInvariant()}.";
                // A clean cancellation is not an error: everything is either done or put back.
                var severity = result.WasCancelled && !result.HasUncertainState
                    ? StatusSeverity.Warning
                    : StatusSeverity.Error;
                SetStatus(message, severity);
            }

            // One-way actions run after the reversible batch, and only when it
            // succeeded; a failed change batch should not be followed by installs.
            if (result.IsSuccess && !cancellationToken.IsCancellationRequested && _pendingActionsService is { PendingCount: > 0 })
            {
                var actionCount = _pendingActionsService.PendingCount;
                SetStatus(
                    $"Running {actionCount} queued action{(actionCount == 1 ? "" : "s")}...",
                    StatusSeverity.Warning);

                // Surface per-action progress in the status bar; the IsApplying
                // guard lets a stale post lose to the final status below.
                PropertyChangedEventHandler progressHandler = (_, args) =>
                {
                    if (args.PropertyName is nameof(IPendingActionsService.CurrentActionDisplay)
                        && _pendingActionsService.CurrentActionDisplay is { } display)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (_pendingActionsService.IsApplying)
                                SetStatus($"Running: {display}...", StatusSeverity.Warning);
                        });
                    }
                };
                _pendingActionsService.PropertyChanged += progressHandler;

                Core.Actions.ActionBatchResult actionResult;
                try
                {
                    actionResult = await _pendingActionsService
                        .ApplyAllAsync(ExecuteActionOnModule).ConfigureAwait(true);
                }
                finally
                {
                    _pendingActionsService.PropertyChanged -= progressHandler;
                }

                if (CurrentContent is SoftwareViewModel softwareVm)
                    softwareVm.ApplyActionResults(actionResult);
                if (CurrentContent is PowerViewModel powerVm)
                    powerVm.ApplyActionResults(actionResult);
                if (CurrentContent is ShellViewModel shellVm)
                    shellVm.ApplyActionResults(actionResult);

                if (!actionResult.IsSuccess)
                {
                    var first = actionResult.Failed[0];
                    SetStatus(
                        $"{actionResult.Failed.Count} action{(actionResult.Failed.Count == 1 ? "" : "s")} failed. {first.Action.DisplayName}: {first.ErrorMessage}",
                        StatusSeverity.Error);
                }
                else if (result.RequiredRestarts.Count > 0)
                {
                    // The restart banner stays visible; keep the status pointing at it.
                    SetStatus("Actions completed. A restart is still needed for some changes", StatusSeverity.Warning);
                }
                else
                {
                    SetStatus(
                        $"{actionResult.Succeeded.Count} action{(actionResult.Succeeded.Count == 1 ? "" : "s")} completed",
                        StatusSeverity.Success);
                }
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Changes stopped before the next write.", StatusSeverity.Warning);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Apply could not complete coordination or persistence");
            SetStatus("Changes stopped. " + ex.Message, StatusSeverity.Error);
        }
        finally
        {
            _activeBrokerSession = null;
            IsApplying = false;
            PendingCount = _pendingChangesService.PendingCount;
            if (_pendingActionsService is not null)
                ActionCount = _pendingActionsService.PendingCount;
        }
    }

    // Legacy module name mappings for change history entries created before module renames
    private static readonly Dictionary<string, string> LegacyModuleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Shell & Explorer"] = "Explorer",
    };

    private IModule? ResolveModule(string moduleId)
    {
        var module = _navigationService.Modules
            .FirstOrDefault(m => m.Module.Info.Name == moduleId)?.Module;

        if (module is null && LegacyModuleNames.TryGetValue(moduleId, out var currentName))
        {
            module = _navigationService.Modules
                .FirstOrDefault(m => m.Module.Info.Name == currentName)?.Module;
        }

        return module;
    }

    private Task<OperationResult<bool>> ApplyChangeToModule(ChangeDescriptor change) =>
        Logged("Apply", change.ModuleId, change.SettingId, DescribeChange(change), () =>
            RunChange(change, revert: false));

    private Task<OperationResult<bool>> RevertChangeOnModule(ChangeDescriptor change) =>
        Logged("Revert", change.ModuleId, change.SettingId, DescribeChange(change), () =>
            RunChange(change, revert: true));

    private async Task<OperationResult<bool>> RunChange(ChangeDescriptor change, bool revert)
    {
        // Profile files use the desktop account's existing access. Never elevate
        // a caller-supplied file path through the system settings broker.
        if (IsLocalCoolingProfile(change) && ResolveModule(change.ModuleId) is CoolingModule cooling)
            return revert
                ? await cooling.RevertChangeAsync(change).ConfigureAwait(false)
                : await cooling.ApplyChangeAsync(change).ConfigureAwait(false);

        if (_activeBrokerSession is not null)
        {
            return revert
                ? await _activeBrokerSession.RevertChangeAsync(change).ConfigureAwait(false)
                : await _activeBrokerSession.ApplyChangeAsync(change).ConfigureAwait(false);
        }

        if (_privilegeBroker is not null)
        {
            var opened = await _privilegeBroker.OpenSessionAsync(new Ipc.Contracts.BrokerSessionRequest
            {
                Changes = [change],
            }).ConfigureAwait(false);
            if (!opened.IsSuccess)
                return OperationResult<bool>.Failure(opened.ErrorMessage!, opened.ErrorCategory ?? ErrorCategory.AccessDenied);
            await using var session = opened.Value!;
            return revert
                ? await session.RevertChangeAsync(change).ConfigureAwait(false)
                : await session.ApplyChangeAsync(change).ConfigureAwait(false);
        }

        var module = ResolveModule(change.ModuleId);
        if (module is null)
        {
            return OperationResult<bool>.Failure(
                $"Module '{change.ModuleId}' not found{(revert ? " for revert" : string.Empty)}",
                ErrorCategory.NotFound);
        }
        return revert
            ? await module.RevertChangeAsync(change).ConfigureAwait(false)
            : await module.ApplyChangeAsync(change).ConfigureAwait(false);
    }

    private bool IsLocalCoolingProfile(ChangeDescriptor change) =>
        Modules.Hardware.Cooling.FanControlProfileStore.IsProfileChange(change)
        && ResolveModule(change.ModuleId) is CoolingModule;

    private static string DescribeChange(ChangeDescriptor change) =>
        $"{change.DisplayName}: '{change.BeforeDisplay}' to '{change.AfterDisplay}' at {change.SystemLocation}";

    /// <summary>
    /// Runs one module call and logs both ends of it: what was asked, then
    /// the result with its category, message, exception, and elapsed time.
    /// The log therefore holds the full text of every error the status bar
    /// shows, plus the ones the bar had no room for.
    /// </summary>
    private static async Task<OperationResult<bool>> Logged(
        string verb, string moduleId, string id, string detail, Func<Task<OperationResult<bool>>> run)
    {
        Log.Debug("{Verb} {Module}/{Id}: {Detail}", verb, moduleId, id, detail);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        OperationResult<bool> result;
        try
        {
            result = await run().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Verb} {Module}/{Id} threw after {Ms} ms", verb, moduleId, id, clock.ElapsedMilliseconds);
            throw;
        }

        if (result.IsSuccess)
            Log.Info("{Verb} {Module}/{Id} ok in {Ms} ms", verb, moduleId, id, clock.ElapsedMilliseconds);
        else
            Log.Error(result.Exception, "{Verb} {Module}/{Id} failed after {Ms} ms [{Category}]: {Error}",
                verb, moduleId, id, clock.ElapsedMilliseconds, result.ErrorCategory, result.ErrorMessage);
        return result;
    }

    private Task<OperationResult<bool>> ExecuteActionOnModule(Core.Actions.ActionDescriptor action) =>
        Logged("Action", action.ModuleId, action.ActionId, $"{action.DisplayName}: {action.Detail}", async () =>
        {
            if (_activeBrokerSession is not null)
                return await _activeBrokerSession.ExecuteActionAsync(action).ConfigureAwait(false);
            if (_privilegeBroker is not null)
            {
                var opened = await _privilegeBroker.OpenSessionAsync(new Ipc.Contracts.BrokerSessionRequest
                {
                    Actions = [action],
                }).ConfigureAwait(false);
                if (!opened.IsSuccess)
                    return OperationResult<bool>.Failure(opened.ErrorMessage!, opened.ErrorCategory ?? ErrorCategory.AccessDenied);
                await using var session = opened.Value!;
                return await session.ExecuteActionAsync(action).ConfigureAwait(false);
            }
            return ResolveModule(action.ModuleId) is Core.Modules.IActionModule actionModule
                ? await actionModule.ExecuteActionAsync(action).ConfigureAwait(false)
                : OperationResult<bool>.Failure(
                    $"Module '{action.ModuleId}' not found or cannot execute actions", ErrorCategory.NotFound);
        });

    private async Task<RestorePointResult> CreateBrokerRestorePoint(string description)
    {
        var opened = await _privilegeBroker!.OpenSessionAsync(new Ipc.Contracts.BrokerSessionRequest
        {
            RestorePointDescription = description,
        }).ConfigureAwait(true);
        if (!opened.IsSuccess)
        {
            return new()
            {
                Outcome = RestorePointOutcome.Failed,
                Message = opened.ErrorMessage,
            };
        }
        await using var session = opened.Value!;
        return await session.CreateRestorePointAsync(description).ConfigureAwait(true);
    }

    /// <summary>
    /// Shows the restart banner for the changes that completed (on every exit,
    /// not only success: a group that finished before a later one failed still
    /// needs its restart). Returns the short status phrase, or null when nothing
    /// completed needs one.
    /// </summary>
    private string? ShowRestartNotice(IReadOnlyList<RestartRequirement> restarts)
    {
        if (restarts.Contains(RestartRequirement.Reboot))
        {
            // Keep the Explorer-restart action when the batch also needs it, so
            // deferring the reboot doesn't leave Explorer-bound changes inactive.
            var alsoExplorer = restarts.Contains(RestartRequirement.ExplorerRestart);
            RestartNotificationMessage = alsoExplorer
                ? "A reboot is required for some changes; others take effect after an Explorer restart."
                : "A reboot is required for some changes to take effect.";
            IsRestartActionAvailable = alsoExplorer;
            IsRestartNotificationVisible = true;
            return "Reboot required";
        }

        if (restarts.Contains(RestartRequirement.SignOut))
        {
            RestartNotificationMessage = "Sign out and back in for some changes to take effect.";
            IsRestartActionAvailable = false;
            IsRestartNotificationVisible = true;
            return "Sign-out required";
        }

        if (restarts.Contains(RestartRequirement.ExplorerRestart))
        {
            RestartNotificationMessage = "Explorer restart required for changes to take effect. Open file explorer windows may close.";
            IsRestartActionAvailable = true;
            IsRestartNotificationVisible = true;
            return "Explorer restart needed";
        }

        if (restarts.Contains(RestartRequirement.ExplorerRefresh))
        {
            // Fire-and-forget: trigger SHChangeNotify to refresh Explorer views
            _ = _explorerRestartService.RefreshExplorerViewsAsync();

            RestartNotificationMessage = "Explorer preferences updated. Open windows may need F5 to refresh";
            IsRestartActionAvailable = false;
            IsRestartNotificationVisible = true;
            return "Explorer refresh may be needed";
        }

        return null;
    }

    /// <summary>
    /// The status line for a batch that did not finish, written for the person
    /// at the screen: what completed, what is in an unknown state, and what to
    /// click next. Branches on <see cref="MutationResult.FailureKind"/>;
    /// <see cref="MutationResult.Failed"/> is null on cancellation and may be
    /// null on a refusal, so nothing here assumes it.
    /// </summary>
    public static string FormatApplyError(MutationResult result, IReadOnlyList<string> unresolvedGroupNames)
    {
        var completed = result.Applied.Count switch
        {
            0 => "Nothing else was changed.",
            1 => "1 change before it completed and is in History.",
            var n => $"{n} changes before it completed and are in History.",
        };

        switch (result.FailureKind)
        {
            case MutationFailureKind.Cancelled:
            {
                var done = result.Applied.Count switch
                {
                    0 => "Nothing was changed",
                    1 => "1 change completed and is in History",
                    var n => $"{n} changes completed and are in History",
                };
                var putBack = result.RolledBack.Count switch
                {
                    0 => "",
                    1 => "; 1 change was put back",
                    var n => $"; {n} changes were put back",
                };
                if (!result.HasUncertainState)
                    return $"Apply cancelled. {done}{putBack}.";

                return $"Apply cancelled, but {NameList(result.Uncertain)} could not be put back and may be half-changed. "
                    + $"{done}{putBack}. Open the review panel for what to do next.";
            }

            case MutationFailureKind.ReconciliationRequired:
            {
                var groups = unresolvedGroupNames.Count > 0
                    ? Quote(unresolvedGroupNames)
                    : result.Uncertain.Count > 0 ? NameList(result.Uncertain) : "A staged change";
                return $"{groups} did not finish last time and is in an unknown state, so nothing was applied. "
                    + "Click Discard All, then set the change again from the reloaded page.";
            }

            case MutationFailureKind.ChangeFailed:
            case MutationFailureKind.ChangeThrew:
            {
                var name = result.Failed?.DisplayName
                    ?? (result.Uncertain.Count > 0 ? result.Uncertain[0].DisplayName : null);
                var head = name is null ? "A change could not be applied" : $"\"{name}\" could not be applied";
                var reason = result.ErrorMessage is { Length: > 0 } text ? $": {text.TrimEnd('.')}." : ".";
                var guidance = result.ErrorCategory is { } category
                    ? " " + Helpers.ErrorCategoryExtensions.ToGuidance(category)
                    : "";
                var putBack = result.RolledBack.Count switch
                {
                    0 => "",
                    1 => " 1 change in the same group was put back.",
                    var n => $" {n} changes in the same group were put back.",
                };
                var stuck = result.RollbackFailures.Count == 0
                    ? ""
                    : $" {NameList(result.RollbackFailures.Select(f => f.Change).ToList())} could not be put back.";
                return $"{head}{reason}{guidance} Its current value is unknown.{putBack}{stuck} {completed} "
                    + "Open the review panel, click Discard All, then set it again from the reloaded page.";
            }

            default:
            {
                var parts = new List<string>();
                if (result.Failed is not null)
                    parts.Add($"\"{result.Failed.DisplayName}\" could not be applied");
                if (result.ErrorMessage is not null)
                    parts.Add(result.ErrorMessage);
                if (result.ErrorCategory is not null)
                    parts.Add(Helpers.ErrorCategoryExtensions.ToGuidance(result.ErrorCategory.Value));
                parts.Add(completed);
                return string.Join(" ", parts);
            }
        }
    }

    /// <summary>"A", "A and B", or "A, B, and 2 more"; always quoted display names.</summary>
    public static string NameList(IReadOnlyList<ChangeDescriptor> changes)
        => Quote(changes.Select(c => c.DisplayName).ToList());

    private static string Quote(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => $"\"{names[0]}\"",
        2 => $"\"{names[0]}\" and \"{names[1]}\"",
        _ => $"\"{names[0]}\", \"{names[1]}\", and {names.Count - 2} more",
    };

    private void SyncSelectedModule()
    {
        var current = _navigationService.CurrentModule;

        foreach (var group in SidebarGroups)
        {
            foreach (var sidebarItem in group.Items)
            {
                sidebarItem.IsActive = current is not null
                    && sidebarItem.Module == current.Module;
            }
        }

        if (current is not null)
        {
            SelectedModule = SidebarGroups
                .SelectMany(g => g.Items)
                .FirstOrDefault(i => i.Module == current.Module);
        }
    }
}
