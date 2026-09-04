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
    private object? _currentContent;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingChanges))]
    [NotifyPropertyChangedFor(nameof(PendingCountText))]
    [NotifyPropertyChangedFor(nameof(TotalPendingCount))]
    [NotifyPropertyChangedFor(nameof(CanModifyPending))]
    private int _pendingCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingChanges))]
    [NotifyPropertyChangedFor(nameof(PendingCountText))]
    [NotifyPropertyChangedFor(nameof(TotalPendingCount))]
    [NotifyPropertyChangedFor(nameof(CanModifyPending))]
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
        Services.AutorunEnrichment? autorunEnrichment = null)
    {
        _wingetService = wingetService;
        _autorunEnrichment = autorunEnrichment;
        _pendingActionsService = pendingActionsService;
        _ownerModeService = ownerModeService;
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
        _moduleSettingsContributors = moduleSettingsContributors?.ToList() ?? [];
        _updateService = updateService;
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
            RevertChangeOnModule,
            ApplyChangeToModule,
            customSetWriter);

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
        _navigationService.PropertyChanged += OnNavigationPropertyChanged;
        PendingCount = _pendingChangesService.PendingCount;
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
    }

    private async void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(NavigationService.CurrentModule))
            return;

        var epoch = 0;
        try
        {
            epoch = ++_contentEpoch;
            var current = _navigationService.CurrentModule;

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
                            _displayModeStore, _capabilityDetector, _ownerModeService);
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
                            _displayModeStore, _capabilityDetector, _ownerModeService);
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
                            _displayModeStore, _capabilityDetector, _ownerModeService);
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
            else if (current?.Module is Modules.Display.DisplayModule)
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
                        CurrentContent = new DisplayViewModel(displayData, _monitorService, _powerService);
                    }
                    else
                    {
                        CurrentContent = null;
                        SetStatus(scanResult.ErrorMessage ?? "Failed to scan displays", StatusSeverity.Error);
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
    private string? _pendingSearchFocusName;
    private int _pendingSearchFocusEpoch;

    [RelayCommand]
    private void SelectSearchResult(SearchResultViewModel? result)
    {
        if (result is null || !result.IsAvailable)
            return;

        SearchQuery = string.Empty;
        _pendingSearchFocusName = result.Name;
        var epochBefore = _contentEpoch;
        NavigateToModuleByName(result.ModuleId);
        // Any rebuild bumps the epoch synchronously before this line. An
        // unchanged epoch means no rebuild is coming (the target module is the
        // page already on screen), so the live page consumes the focus here.
        _pendingSearchFocusEpoch = _contentEpoch;
        if (_contentEpoch == epochBefore && _pendingSearchFocusName is not null && CurrentContent is not null)
            ApplySearchFocus(CurrentContent);
    }

    partial void OnCurrentContentChanged(object? value)
    {
        if (_pendingSearchFocusName is null)
            return;
        if (_contentEpoch != _pendingSearchFocusEpoch)
        {
            // A different navigation owns the content now; the moment has passed.
            _pendingSearchFocusName = null;
            return;
        }
        // Content flips to null while the module scans; only real content consumes.
        if (value is not null)
            ApplySearchFocus(value);
    }

    private void ApplySearchFocus(object content)
    {
        var focus = _pendingSearchFocusName!;
        _pendingSearchFocusName = null;
        if (content is ISearchFocusTarget target)
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
        IsGalleryActive = false;
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

    [ObservableProperty]
    private bool _isUpdateBadgeVisible;

    [ObservableProperty]
    private string _updateBadgeText = string.Empty;

    private async Task CheckForUpdateBadgeAsync()
    {
        if (_updateService is null)
            return;
        // Opt-out toggle (default on): off means NO network request at all.
        if (_settingsService is not null
            && !_settingsService.GetAppBool(Core.Settings.AppSettingKeys.UpdateCheck, fallback: true))
        {
            return;
        }

        try
        {
            var result = await _updateService.CheckForUpdateAsync().ConfigureAwait(true);
            if (result.IsSuccess && result.Value is { IsAvailable: true, Version: { } version })
            {
                UpdateBadgeText = $"Update {version}";
                IsUpdateBadgeVisible = true;
                _notificationService?.Notify(
                    Core.Notifications.NotificationType.UpdateAvailable,
                    "Update available",
                    $"ThisIsMyPC {version} is available - click the badge to open the releases page");
            }
        }
#pragma warning disable CA1031 // AC: check failures are silently ignored; offline-safe
        catch (Exception ex)
        {
            Log.Warn(ex, "Update check failed");
        }
#pragma warning restore CA1031
    }

    [RelayCommand]
    private void OpenReleasesPage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Core.AppConstants.UpdateUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus($"Could not open the releases page: {Core.AppConstants.UpdateUrl}", StatusSeverity.Warning);
        }
    }

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
            appVersion: typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            capabilityReport: _capabilityDetector?.GetCapabilityReport(),
            ownerMode: _ownerModeService is { } ownerMode ? new OwnerModeSectionViewModel(ownerMode) : null);
        IsSettingsActive = true;
        IsGalleryActive = false;
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
            CurrentContent = new DisplayViewModel(data, _monitorService, _powerService);
    }

    [ObservableProperty]
    private bool _isGalleryActive;

    /// <summary>The UI Gallery is a dev-facing style reference; Release builds hide it.</summary>
#if DEBUG
    public static bool IsGalleryVisible => true;
#else
    public static bool IsGalleryVisible => false;
#endif

    /// <summary>Dev-facing style reference (UI Gallery); Debug builds only.</summary>
    [RelayCommand]
    private void OpenGallery()
    {
        _contentEpoch++;
        IsModuleLoading = false;
        (CurrentContent as IDisposable)?.Dispose();

        ContentTitle = "UI Gallery";
        ContentDescription = "Every standardized style and token on one page";
        CurrentContent = new GalleryViewModel();
        IsGalleryActive = true;
        IsSettingsActive = false;
        IsSetLoaderActive = false;
        IsHomeActive = false;
        SelectedModule = null;
        ClearSidebarActives();
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

        var quickActions = SidebarGroups
            .SelectMany(g => g.Items)
            .Where(i => i.IsAvailable)
            .Select(i => new QuickActionViewModel(i.Name, () => i.IconGeometry, () => NavigateToModule(i)))
            .ToList();

        var home = new HomeViewModel(
            new SystemIdentityService(_registryService).Read(),
            quickActions,
            _changeHistoryService,
            BuildFirstLaunchBanner(),
            BuildMonitoringSection(),
            _driftSection);
        CurrentContent = home;
        IsHomeActive = true;
        IsGalleryActive = false;
        IsSetLoaderActive = false;
        IsSettingsActive = false;
        SelectedModule = null;
        ClearSidebarActives();

        // Recent activity fills in asynchronously; the dashboard never blocks.
        _ = home.LoadRecentActivityCommand.ExecuteAsync(null);
    }

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

    [RelayCommand]
    private void DiscardAll()
    {
        _pendingChangesService.DiscardAll();
        _pendingActionsService?.DiscardAll();
        _applyWithoutRestorePoint = false;
        IsReviewPanelOpen = false;
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
            var result = await _restorePointService.CreateRestorePointAsync(
                $"ThisIsMyPC restore point {DateTime.Now:yyyy-MM-dd HH:mm}").ConfigureAwait(true);

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
    private async Task ApplyAllAsync()
    {
        if (!HasPendingChanges || IsApplying || IsCreatingRestorePoint)
            return;

        IsApplying = true;
        StatusMessage = string.Empty;

        try
        {
            // FR64: safety net before bulk batches. Counts individual descriptors;
            // PendingCount counts groups, so a single 6-change set must still trigger.
            // One-way actions count too: a bulk of Appx removals is the least
            // reversible thing in the app and deserves the restore point most.
            var changeCount = _pendingChangesService.PendingGroups.Sum(g => g.Changes.Count)
                + (_pendingActionsService?.PendingCount ?? 0);
            if (changeCount >= AutoRestorePointThreshold && !_applyWithoutRestorePoint)
            {
                SetStatus("Creating restore point...", StatusSeverity.Warning);
                var restorePoint = await _restorePointService.CreateRestorePointAsync(
                    $"ThisIsMyPC: Before applying {changeCount} changes").ConfigureAwait(true);

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

            Log.Info("Apply: {Groups} group(s), {Changes} change(s), {Actions} action(s)",
                _pendingChangesService.PendingGroups.Count,
                _pendingChangesService.PendingGroups.Sum(g => g.Changes.Count),
                _pendingActionsService?.PendingCount ?? 0);

            var result = await _pendingChangesService.ApplyAllAsync(
                ApplyChangeToModule,
                RevertChangeOnModule).ConfigureAwait(true);

            if (result.IsSuccess)
            {
                Log.Info("Apply: {Count} change(s) applied; restarts needed: {Restarts}",
                    result.Applied.Count, string.Join(", ", result.RequiredRestarts));
            }
            else
            {
                Log.Error("Apply stopped at {Module}/{Setting} ({Location}) [{Category}]: {Error}; {Applied} applied before it, {RolledBack} rolled back",
                    result.Failed?.ModuleId, result.Failed?.SettingId, result.Failed?.SystemLocation,
                    result.ErrorCategory, result.ErrorMessage, result.Applied.Count, result.RolledBack.Count);
            }

            if (result.IsSuccess)
            {
                Log.Info("Apply: {Count} change(s) applied; restarts needed: {Restarts}",
                    result.Applied.Count, string.Join(", ", result.RequiredRestarts));
            }
            else
            {
                Log.Error("Apply stopped at {Module}/{Setting} ({Location}) [{Category}]: {Error}; {Applied} applied before it, {RolledBack} rolled back",
                    result.Failed?.ModuleId, result.Failed?.SettingId, result.Failed?.SystemLocation,
                    result.ErrorCategory, result.ErrorMessage, result.Applied.Count, result.RolledBack.Count);
            }

            if (result.IsSuccess)
            {
                _applyWithoutRestorePoint = false;
                await _changeHistoryService.RecordChangesAsync(result).ConfigureAwait(true);
                IsReviewPanelOpen = false;

                if (result.RequiredRestarts.Contains(RestartRequirement.Reboot))
                {
                    // Keep the Explorer-restart action when the batch also needs it, so
                    // deferring the reboot doesn't leave Explorer-bound changes inactive.
                    var alsoExplorer = result.RequiredRestarts.Contains(RestartRequirement.ExplorerRestart);
                    RestartNotificationMessage = alsoExplorer
                        ? "A reboot is required for some changes; others take effect after an Explorer restart."
                        : "A reboot is required for some changes to take effect.";
                    IsRestartActionAvailable = alsoExplorer;
                    IsRestartNotificationVisible = true;
                    SetStatus("Changes applied. Reboot required", StatusSeverity.Warning);
                }
                else if (result.RequiredRestarts.Contains(RestartRequirement.SignOut))
                {
                    RestartNotificationMessage = "Sign out and back in for some changes to take effect.";
                    IsRestartActionAvailable = false;
                    IsRestartNotificationVisible = true;
                    SetStatus("Changes applied. Sign-out required", StatusSeverity.Warning);
                }
                else if (result.RequiredRestarts.Contains(RestartRequirement.ExplorerRestart))
                {
                    RestartNotificationMessage = "Explorer restart required for changes to take effect. Open file explorer windows may close.";
                    IsRestartActionAvailable = true;
                    IsRestartNotificationVisible = true;
                    SetStatus("Changes applied. Explorer restart needed", StatusSeverity.Warning);
                }
                else if (result.RequiredRestarts.Contains(RestartRequirement.ExplorerRefresh))
                {
                    // Fire-and-forget: trigger SHChangeNotify to refresh Explorer views
                    _ = _explorerRestartService.RefreshExplorerViewsAsync();

                    RestartNotificationMessage = "Explorer preferences updated. Open windows may need F5 to refresh";
                    IsRestartActionAvailable = false;
                    IsRestartNotificationVisible = true;
                    SetStatus("Changes applied. Explorer refresh may be needed", StatusSeverity.Success);
                }
                else
                {
                    SetStatus("Changes applied successfully", StatusSeverity.Success);
                }
            }
            else
            {
                SetStatus(FormatApplyError(result), StatusSeverity.Error);
            }

            // One-way actions run after the reversible batch, and only when it
            // succeeded; a failed change batch should not be followed by installs.
            if (result.IsSuccess && _pendingActionsService is { PendingCount: > 0 })
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
        finally
        {
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
        {
            var module = ResolveModule(change.ModuleId);
            return module is null
                ? Task.FromResult(OperationResult<bool>.Failure($"Module '{change.ModuleId}' not found", ErrorCategory.NotFound))
                : module.ApplyChangeAsync(change);
        });

    private Task<OperationResult<bool>> RevertChangeOnModule(ChangeDescriptor change) =>
        Logged("Revert", change.ModuleId, change.SettingId, DescribeChange(change), () =>
        {
            var module = ResolveModule(change.ModuleId);
            return module is null
                ? Task.FromResult(OperationResult<bool>.Failure($"Module '{change.ModuleId}' not found for revert", ErrorCategory.NotFound))
                : module.RevertChangeAsync(change);
        });

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
        Logged("Action", action.ModuleId, action.ActionId, $"{action.DisplayName}: {action.Detail}", () =>
            ResolveModule(action.ModuleId) is Core.Modules.IActionModule actionModule
                ? actionModule.ExecuteActionAsync(action)
                : Task.FromResult(OperationResult<bool>.Failure(
                    $"Module '{action.ModuleId}' not found or cannot execute actions", ErrorCategory.NotFound)));

    private static string FormatApplyError(MutationResult result)
    {
        var parts = new List<string>();

        if (result.Failed is not null)
            parts.Add($"Failed: {result.Failed.DisplayName} ({result.Failed.SystemLocation})");

        if (result.ErrorMessage is not null)
            parts.Add(result.ErrorMessage);

        if (result.ErrorCategory is not null)
            parts.Add(Helpers.ErrorCategoryExtensions.ToGuidance(result.ErrorCategory.Value));

        return parts.Count > 0
            ? string.Join(" - ", parts)
            : "An unknown error occurred while applying changes.";
    }

    [System.Diagnostics.Conditional("DEBUG")]
    public void StageDebugChange(ChangeDescriptor change)
    {
        _pendingChangesService.Stage(change);
    }

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
