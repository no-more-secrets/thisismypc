using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Discovered startup entries grouped by source type, with enable/disable
/// toggles staged through the pending-changes pipeline (StartupApproved state).
/// </summary>
public sealed partial class StartupViewModel : ObservableObject, IDisposable
{
    private readonly List<ServiceItemViewModel> _allServices = [];

    [ObservableProperty]
    private bool _isRegistryViewMode;

    [ObservableProperty]
    private string _serviceFilterText = string.Empty;

    [ObservableProperty]
    private string _serviceSortColumn = "Name";

    [ObservableProperty]
    private bool _serviceSortDescending;

    private readonly List<ScheduledTaskItemViewModel> _allTasks = [];

    [ObservableProperty]
    private string _taskFilterText = string.Empty;

    [ObservableProperty]
    private string _taskClassificationFilter = "All";

    [ObservableProperty]
    private string _taskSortColumn = "Name";

    [ObservableProperty]
    private bool _taskSortDescending;

    public static IReadOnlyList<string> ClassificationFilterOptions { get; } =
        ["All", "Telemetry", "OEM", "Compatibility Diagnostics", "Maintenance", "User-Created", "Unknown"];

    public StartupViewModel(
        StartupScanData scanData,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService,
        IServiceControlService? serviceControlService = null,
        IScheduledTaskService? scheduledTaskService = null,
        Modules.Startup.Services.TaskClassificationOverrideStore? classificationOverrides = null)
    {
        RegistryEntries = new ObservableCollection<StartupEntryItemViewModel>(
            scanData.StartupEntries
                .Where(e => e.Source is StartupSource.RegistryMachineRun or StartupSource.RegistryMachineRunWow64 or StartupSource.RegistryUserRun)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new StartupEntryItemViewModel(e, pendingChangesService, registryService)));
        FolderEntries = new ObservableCollection<StartupEntryItemViewModel>(
            scanData.StartupEntries
                .Where(e => e.Source is StartupSource.StartupFolderUser or StartupSource.StartupFolderCommon)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new StartupEntryItemViewModel(e, pendingChangesService, registryService)));
        TaskEntries = new ObservableCollection<StartupEntryItemViewModel>(
            scanData.StartupEntries
                .Where(e => e.Source == StartupSource.ScheduledTask)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new StartupEntryItemViewModel(e, pendingChangesService, registryService)));

        _allServices.AddRange(scanData.Services
            .Select(s => new ServiceItemViewModel(s, pendingChangesService, serviceControlService)));
        ServicesScanError = scanData.ServicesScanError;
        Services = [];
        RebuildServices();

        _allTasks.AddRange(scanData.ScheduledTasks
            .Select(t => new ScheduledTaskItemViewModel(
                t, pendingChangesService, scheduledTaskService, classificationOverrides, RebuildTasks)));
        ScheduledTasksScanError = scanData.ScheduledTasksScanError;
        ScheduledTaskItems = [];
        RebuildTasks();
    }

    public string? ServicesScanError { get; }
    public string? ScheduledTasksScanError { get; }

    public ObservableCollection<ScheduledTaskItemViewModel> ScheduledTaskItems { get; }

    public string ScheduledTasksSectionHeader => $"All Scheduled Tasks ({ScheduledTaskItems.Count} of {_allTasks.Count})";
    public bool HasVisibleTasks => ScheduledTaskItems.Count > 0;

    partial void OnTaskFilterTextChanged(string value) => RebuildTasks();
    partial void OnTaskClassificationFilterChanged(string value) => RebuildTasks();
    partial void OnTaskSortColumnChanged(string value) => RebuildTasks();
    partial void OnTaskSortDescendingChanged(bool value) => RebuildTasks();

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SortTasks(string column)
    {
        if (TaskSortColumn == column)
            TaskSortDescending = !TaskSortDescending;
        else
        {
            TaskSortColumn = column;
            TaskSortDescending = false;
        }
    }

    private void RebuildTasks()
    {
        IEnumerable<ScheduledTaskItemViewModel> filtered = _allTasks;

        if (TaskClassificationFilter != "All")
            filtered = filtered.Where(t => t.SelectedClassification == TaskClassificationFilter);

        var filter = TaskFilterText.Trim();
        if (filter.Length > 0)
        {
            filtered = filtered.Where(t =>
                t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                t.Path.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                t.PublisherText.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        Func<ScheduledTaskItemViewModel, string> sortKey = TaskSortColumn switch
        {
            "Classification" => t => t.SelectedClassification,
            "Status" => t => t.StateText,
            "LastRun" => t => (t.Entry.LastRunTime ?? DateTime.MinValue).ToString("O"),
            _ => t => t.Name,
        };

        var sorted = TaskSortDescending
            ? filtered.OrderByDescending(sortKey, StringComparer.OrdinalIgnoreCase)
            : filtered.OrderBy(sortKey, StringComparer.OrdinalIgnoreCase);
        var final = sorted.ThenBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToList();

        ScheduledTaskItems.Clear();
        foreach (var item in final)
            ScheduledTaskItems.Add(item);
        OnPropertyChanged(nameof(ScheduledTasksSectionHeader));
        OnPropertyChanged(nameof(HasVisibleTasks));
    }

    public ObservableCollection<ServiceItemViewModel> Services { get; }

    public string ServicesHeader => $"Services ({Services.Count} of {_allServices.Count})";
    public bool HasVisibleServices => Services.Count > 0;

    partial void OnServiceFilterTextChanged(string value) => RebuildServices();
    partial void OnServiceSortColumnChanged(string value) => RebuildServices();
    partial void OnServiceSortDescendingChanged(bool value) => RebuildServices();

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SortServices(string column)
    {
        if (ServiceSortColumn == column)
            ServiceSortDescending = !ServiceSortDescending;
        else
        {
            ServiceSortColumn = column;
            ServiceSortDescending = false;
        }
    }

    private void RebuildServices()
    {
        IEnumerable<ServiceItemViewModel> filtered = _allServices;
        var filter = ServiceFilterText.Trim();
        if (filter.Length > 0)
        {
            filtered = filtered.Where(s =>
                s.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.ServiceName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (s.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Func<ServiceItemViewModel, string> sortKey = ServiceSortColumn switch
        {
            "Status" => s => s.StateText,
            "StartType" => s => s.StartTypeText,
            _ => s => s.DisplayName,
        };

        // Per-user instances sort with their template's key so they stay adjacent
        // (template row first, instances after), regardless of the chosen column.
        var byName = _allServices.ToDictionary(s => s.ServiceName, StringComparer.OrdinalIgnoreCase);
        string RootKey(ServiceItemViewModel s) =>
            s.Entry.TemplateServiceName is not null && byName.TryGetValue(s.Entry.TemplateServiceName, out var template)
                ? sortKey(template)
                : sortKey(s);
        string RootName(ServiceItemViewModel s) => s.Entry.TemplateServiceName ?? s.ServiceName;

        var sorted = ServiceSortDescending
            ? filtered.OrderByDescending(RootKey, StringComparer.OrdinalIgnoreCase)
            : filtered.OrderBy(RootKey, StringComparer.OrdinalIgnoreCase);

        var final = sorted
            .ThenBy(RootName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Entry.IsPerUserInstance)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Services.Clear();
        foreach (var item in final)
            Services.Add(item);
        OnPropertyChanged(nameof(ServicesHeader));
        OnPropertyChanged(nameof(HasVisibleServices));
    }

    public ObservableCollection<StartupEntryItemViewModel> RegistryEntries { get; }
    public ObservableCollection<StartupEntryItemViewModel> FolderEntries { get; }
    public ObservableCollection<StartupEntryItemViewModel> TaskEntries { get; }

    public string RegistryHeader => $"Registry ({RegistryEntries.Count})";
    public string FolderHeader => $"Startup Folder ({FolderEntries.Count})";
    public string TaskHeader => $"Scheduled Tasks ({TaskEntries.Count})";

    public bool HasRegistryEntries => RegistryEntries.Count > 0;
    public bool HasFolderEntries => FolderEntries.Count > 0;
    public bool HasTaskEntries => TaskEntries.Count > 0;

    partial void OnIsRegistryViewModeChanged(bool value)
    {
        foreach (var item in RegistryEntries.Concat(FolderEntries).Concat(TaskEntries))
            item.IsRegistryViewMode = value;
    }

    public void Dispose()
    {
        foreach (var item in RegistryEntries.Concat(FolderEntries).Concat(TaskEntries))
            item.Dispose();
        foreach (var item in _allServices)
            item.Dispose();
        foreach (var item in _allTasks)
            item.Dispose();
    }
}

/// <summary>
/// One scheduled-task row: enable/disable stages through the pending pipeline;
/// classification overrides persist immediately (app metadata, not a system mutation).
/// </summary>
public sealed partial class ScheduledTaskItemViewModel : ObservableObject, IDisposable
{
    private readonly IPendingChangesService _pendingChangesService;
    private readonly IScheduledTaskService? _taskService;
    private readonly Modules.Startup.Services.TaskClassificationOverrideStore? _overrides;
    private readonly Action? _classificationChanged;
    private bool _liveIsEnabled;
    private bool _suppressStaging;
    private bool _suppressOverride;
    private bool _isStagingChange;
    private string? _stagedGroupId;
    private bool _disposed;

    public static IReadOnlyList<string> ClassificationOptions { get; } =
        ["Telemetry", "OEM", "Compatibility Diagnostics", "Maintenance", "User-Created", "Unknown"];

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _selectedClassification;

    [ObservableProperty]
    private bool _hasPendingChange;

    public ScheduledTaskItemViewModel(
        ScheduledTaskEntry entry,
        IPendingChangesService pendingChangesService,
        IScheduledTaskService? taskService,
        Modules.Startup.Services.TaskClassificationOverrideStore? overrides,
        Action? classificationChanged = null)
    {
        Entry = entry;
        _pendingChangesService = pendingChangesService;
        _taskService = taskService;
        _overrides = overrides;
        _classificationChanged = classificationChanged;
        _liveIsEnabled = entry.IsEnabled;

        _suppressStaging = true;
        _suppressOverride = true;
        _isEnabled = entry.IsEnabled;
        _selectedClassification = ToDisplay(entry.Classification);

        // Rehydrate a toggle group staged in an earlier visit
        var settingId = ScheduledTaskChangeFactory.GetSettingId(entry.Path);
        var existing = pendingChangesService.PendingGroups.FirstOrDefault(g =>
            g.Changes.Count == 1 &&
            g.Changes[0].ModuleId == "Startup & Services" &&
            g.Changes[0].SettingId == settingId);
        if (existing is not null)
        {
            var pendingEnabled = existing.Changes[0].Category == ChangeCategory.Enable;
            if (pendingEnabled == _liveIsEnabled)
                pendingChangesService.Unstage(existing.GroupId);
            else
            {
                _stagedGroupId = existing.GroupId;
                _isEnabled = pendingEnabled;
            }
        }
        _suppressStaging = false;
        _suppressOverride = false;
        UpdatePendingState();

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    public ScheduledTaskEntry Entry { get; }

    public string Name => Entry.Name;
    public string Path => Entry.Path;
    public string PublisherText => Entry.Author ?? "Unknown publisher";
    public string DescriptionText => Entry.Description ?? string.Empty;
    public bool HasDescription => !string.IsNullOrEmpty(Entry.Description);
    public string TriggersText => Entry.TriggerTypes.Count > 0 ? string.Join(", ", Entry.TriggerTypes) : "No triggers";
    public string LastRunText => Entry.LastRunTime?.ToString("g") ?? "Never";
    public string LastResultText => Entry.LastTaskResult == 0 ? "OK" : $"0x{Entry.LastTaskResult:X8}";
    public bool IsCompanionTask => Entry.IsCompanionTask;
    public string CompanionDescription => Entry.CompanionDescription ?? string.Empty;
    public string StateText => IsEnabled ? "Enabled" : "Disabled";

    private static string ToDisplay(TaskClassification classification) => classification switch
    {
        TaskClassification.Telemetry => "Telemetry",
        TaskClassification.Oem => "OEM",
        TaskClassification.CompatibilityDiagnostics => "Compatibility Diagnostics",
        TaskClassification.Maintenance => "Maintenance",
        TaskClassification.UserCreated => "User-Created",
        _ => "Unknown",
    };

    private static TaskClassification FromDisplay(string display) => display switch
    {
        "Telemetry" => TaskClassification.Telemetry,
        "OEM" => TaskClassification.Oem,
        "Compatibility Diagnostics" => TaskClassification.CompatibilityDiagnostics,
        "Maintenance" => TaskClassification.Maintenance,
        "User-Created" => TaskClassification.UserCreated,
        _ => TaskClassification.Unknown,
    };

    partial void OnSelectedClassificationChanged(string value)
    {
        if (_suppressOverride || _disposed)
            return;

        _overrides?.Set(Entry.Path, FromDisplay(value));
        // Deferred so the ComboBox interaction completes before the list refilters
        // (an active classification filter may remove this row).
        if (_classificationChanged is not null)
            Dispatcher.UIThread.Post(_classificationChanged.Invoke);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressStaging || _disposed)
            return;

        OnPropertyChanged(nameof(StateText));

        // Cancel any in-flight staging so only the final toggle state wins
        _toggleCts?.Cancel();
        _toggleCts?.Dispose();
        _toggleCts = new CancellationTokenSource();
        _ = StageToggleAsync(value, _toggleCts.Token);
    }

    private CancellationTokenSource? _toggleCts;

    // Runs on the UI thread; the COM Query runs on the thread pool and the await
    // resumes on the UI thread, so staging and property updates stay dispatcher-safe.
    private async Task StageToggleAsync(bool value, CancellationToken token)
    {
        try
        {
            // Refresh baseline from the live task state when available
            if (_taskService is not null)
            {
                var live = await Task.Run(() => _taskService.Query(Entry.Path));
                if (token.IsCancellationRequested || _disposed)
                    return;
                if (live.IsSuccess && live.Value is not null)
                    _liveIsEnabled = live.Value.IsEnabled;
            }

            _isStagingChange = true;
            try
            {
                if (_stagedGroupId is not null)
                {
                    _pendingChangesService.Unstage(_stagedGroupId);
                    _stagedGroupId = null;
                }

                if (value != _liveIsEnabled)
                {
                    var change = ScheduledTaskChangeFactory.CreateToggle(
                        Entry with { IsEnabled = _liveIsEnabled }, value);
                    var group = new ChangeGroup
                    {
                        GroupId = Guid.NewGuid().ToString("N"),
                        DisplayName = change.DisplayName,
                        Description = change.DisplayName,
                        Changes = [change],
                    };
                    _pendingChangesService.Stage(group);
                    _stagedGroupId = group.GroupId;
                }
            }
            finally
            {
                _isStagingChange = false;
            }

            UpdatePendingState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Task toggle staging failed for {Name}: {ex.Message}");
        }
    }

    private void OnPendingChangesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isStagingChange)
            return;
        if (e.PropertyName is not nameof(IPendingChangesService.PendingGroups))
            return;

        if (Dispatcher.UIThread.CheckAccess())
            HandlePendingGroupsChanged();
        else
            Dispatcher.UIThread.Post(HandlePendingGroupsChanged);
    }

    private void HandlePendingGroupsChanged()
    {
        if (_stagedGroupId is not null &&
            !_pendingChangesService.PendingGroups.Any(g => g.GroupId == _stagedGroupId))
        {
            _stagedGroupId = null;

            if (_pendingChangesService.IsApplying)
            {
                _liveIsEnabled = IsEnabled;
            }
            else
            {
                _suppressStaging = true;
                IsEnabled = _liveIsEnabled;
                _suppressStaging = false;
                OnPropertyChanged(nameof(StateText));
            }

            UpdatePendingState();
        }
    }

    private void UpdatePendingState()
    {
        HasPendingChange = IsEnabled != _liveIsEnabled;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
        _toggleCts?.Cancel();
        _toggleCts?.Dispose();
        _toggleCts = null;
    }
}

/// <summary>
/// One service row: startup-type changes stage through the pending pipeline;
/// Start/Stop/Restart execute immediately against the SCM and are NOT recorded
/// in change history (transient operational actions).
/// </summary>
public sealed partial class ServiceItemViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(30);

    private readonly IPendingChangesService _pendingChangesService;
    private readonly IServiceControlService? _serviceControl;
    private ServiceStartType _liveStartType;
    private bool _suppressStaging;
    private bool _isStagingChange;
    private string? _stagedGroupId;
    private bool _disposed;

    public static IReadOnlyList<string> StartTypeOptions { get; } =
        ["Automatic", "Automatic (Delayed)", "Manual", "Disabled"];

    [ObservableProperty]
    private string _selectedStartTypeOption;

    [ObservableProperty]
    private string _stateText;

    [ObservableProperty]
    private bool _hasPendingChange;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _actionError;

    public ServiceItemViewModel(ServiceEntry entry, IPendingChangesService pendingChangesService, IServiceControlService? serviceControl)
    {
        Entry = entry;
        _pendingChangesService = pendingChangesService;
        _serviceControl = serviceControl;
        _liveStartType = entry.StartType;
        _stateText = entry.State.ToString();

        _suppressStaging = true;
        _selectedStartTypeOption = ToOption(entry.StartType);

        // Rehydrate a start-type group staged in an earlier visit
        var settingId = ServiceChangeFactory.GetSettingId(entry.ServiceName);
        var existing = pendingChangesService.PendingGroups.FirstOrDefault(g =>
            g.Changes.Count == 1 &&
            g.Changes[0].ModuleId == "Startup & Services" &&
            g.Changes[0].SettingId == settingId);
        if (existing is not null &&
            Enum.TryParse<ServiceStartType>(existing.Changes[0].AfterValue, out var pendingType))
        {
            if (pendingType == _liveStartType)
            {
                // Pending target already matches live state; drop the redundant group
                pendingChangesService.Unstage(existing.GroupId);
            }
            else
            {
                _stagedGroupId = existing.GroupId;
                _selectedStartTypeOption = ToOption(pendingType);
            }
        }
        _suppressStaging = false;
        UpdatePendingState();

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    public ServiceEntry Entry { get; }

    public string ServiceName => Entry.ServiceName;
    public string DisplayName => Entry.DisplayName;
    public string? Description => Entry.Description;
    public string DescriptionText => Entry.Description ?? string.Empty;
    public bool IsPerUserInstance => Entry.IsPerUserInstance;
    public string PerUserLabel => Entry.TemplateServiceName is null
        ? string.Empty
        : $"Per-user instance of {Entry.TemplateServiceName}";
    public string StartTypeText => ToOption(_liveStartType);

    /// <summary>Per-user template instances are managed via their template service.</summary>
    public bool CanChangeStartType => !Entry.IsPerUserInstance;

    private static string ToOption(ServiceStartType startType) => ServiceChangeFactory.Describe(startType);

    private static ServiceStartType FromOption(string option) => option switch
    {
        "Automatic" => ServiceStartType.Automatic,
        "Automatic (Delayed)" => ServiceStartType.AutomaticDelayed,
        "Disabled" => ServiceStartType.Disabled,
        _ => ServiceStartType.Manual,
    };

    partial void OnSelectedStartTypeOptionChanged(string value)
    {
        if (_suppressStaging || _disposed || !CanChangeStartType)
            return;

        try
        {
            var desired = FromOption(value);

            // Refresh baseline from the SCM (source of truth) when available
            if (_serviceControl is not null)
            {
                var live = _serviceControl.Query(Entry.ServiceName);
                if (live.IsSuccess && live.Value is not null)
                    _liveStartType = live.Value.StartType;
            }

            _isStagingChange = true;
            try
            {
                if (_stagedGroupId is not null)
                {
                    _pendingChangesService.Unstage(_stagedGroupId);
                    _stagedGroupId = null;
                }

                if (desired != _liveStartType)
                {
                    var change = ServiceChangeFactory.CreateStartTypeChange(
                        Entry with { StartType = _liveStartType }, desired);
                    var group = new ChangeGroup
                    {
                        GroupId = Guid.NewGuid().ToString("N"),
                        DisplayName = change.DisplayName,
                        Description = change.DisplayName,
                        Changes = [change],
                    };
                    _pendingChangesService.Stage(group);
                    _stagedGroupId = group.GroupId;
                }
            }
            finally
            {
                _isStagingChange = false;
            }

            UpdatePendingState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Start-type staging failed for {DisplayName}: {ex.Message}");
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task StartAsync() =>
        RunActionAsync("start", sc => sc.StartAsync(Entry.ServiceName, ActionTimeout));

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task StopAsync() =>
        RunActionAsync("stop", sc => sc.StopAsync(Entry.ServiceName, ActionTimeout));

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task RestartAsync()
    {
        await RunActionAsync("restart", async sc =>
        {
            var stop = await sc.StopAsync(Entry.ServiceName, ActionTimeout);
            if (!stop.IsSuccess)
                return stop;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StateText = "Restarting…");
            return await sc.StartAsync(Entry.ServiceName, ActionTimeout);
        });
    }

    // Invoked on the UI thread (RelayCommand); the SCM work runs on the thread
    // pool via Task.Run and every await resumes on the UI thread, so property
    // updates stay dispatcher-safe.
    private async Task RunActionAsync(
        string verb, Func<IServiceControlService, Task<Core.Results.OperationResult<bool>>> action)
    {
        if (_serviceControl is null || IsBusy)
            return;

        IsBusy = true;
        ActionError = null;
        try
        {
            var result = await Task.Run(() => action(_serviceControl));
            var refreshed = await Task.Run(() => _serviceControl.Query(Entry.ServiceName));
            if (refreshed.IsSuccess && refreshed.Value is not null)
                StateText = refreshed.Value.State.ToString();
            if (!result.IsSuccess)
                ActionError = result.ErrorMessage ?? $"Failed to {verb} {DisplayName}";
        }
        catch (Exception ex)
        {
            ActionError = $"Failed to {verb} {DisplayName}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnPendingChangesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isStagingChange)
            return;
        if (e.PropertyName is not nameof(IPendingChangesService.PendingGroups))
            return;

        if (Dispatcher.UIThread.CheckAccess())
            HandlePendingGroupsChanged();
        else
            Dispatcher.UIThread.Post(HandlePendingGroupsChanged);
    }

    private void HandlePendingGroupsChanged()
    {
        if (_stagedGroupId is not null &&
            !_pendingChangesService.PendingGroups.Any(g => g.GroupId == _stagedGroupId))
        {
            _stagedGroupId = null;

            if (_pendingChangesService.IsApplying)
            {
                _liveStartType = FromOption(SelectedStartTypeOption);
                OnPropertyChanged(nameof(StartTypeText));
            }
            else
            {
                _suppressStaging = true;
                SelectedStartTypeOption = ToOption(_liveStartType);
                _suppressStaging = false;
            }

            UpdatePendingState();
        }
    }

    private void UpdatePendingState()
    {
        HasPendingChange = FromOption(SelectedStartTypeOption) != _liveStartType;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
    }
}

public sealed partial class StartupEntryItemViewModel : ObservableObject, IDisposable
{
    private readonly IPendingChangesService _pendingChangesService;
    private readonly IRegistryService _registryService;
    private bool _liveIsEnabled;
    private bool _suppressStaging;
    private bool _isStagingChange;
    private string? _stagedGroupId;
    private bool _disposed;

    [ObservableProperty]
    private bool _isRegistryViewMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _hasPendingChange;

    [ObservableProperty]
    private bool _isPendingEnable;

    [ObservableProperty]
    private bool _isPendingDisable;

    public StartupEntryItemViewModel(StartupEntry entry, IPendingChangesService pendingChangesService, IRegistryService registryService)
    {
        Entry = entry;
        _pendingChangesService = pendingChangesService;
        _registryService = registryService;
        _liveIsEnabled = entry.IsEnabled;

        _suppressStaging = true;
        IsEnabled = entry.IsEnabled;

        // Rehydrate a group this module staged in an earlier visit so re-navigation
        // shows the pending state instead of double-staging the same entry.
        var settingId = StartupChangeFactory.GetSettingId(entry);
        var existing = pendingChangesService.PendingGroups.FirstOrDefault(g =>
            g.Changes.Count == 1 &&
            g.Changes[0].ModuleId == "Startup & Services" &&
            g.Changes[0].SettingId == settingId);
        if (existing is not null)
        {
            var pendingEnabled = existing.Changes[0].Category == ChangeCategory.Enable;
            if (pendingEnabled == _liveIsEnabled)
            {
                // Pending target already matches live state; drop the redundant group
                pendingChangesService.Unstage(existing.GroupId);
            }
            else
            {
                _stagedGroupId = existing.GroupId;
                IsEnabled = pendingEnabled;
            }
        }

        _suppressStaging = false;
        UpdatePendingState();

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    public StartupEntry Entry { get; }

    public string Name => Entry.Name;
    public string PublisherText => Entry.Publisher ?? "Unknown publisher";
    public string DescriptionText => Entry.Description ?? string.Empty;
    public bool HasDescription => !string.IsNullOrEmpty(Entry.Description);
    public string StateText => IsEnabled ? "Enabled" : "Disabled";

    /// <summary>Scheduled-task entries can't be toggled until Story 3.4's COM interop.</summary>
    public bool CanToggle => StartupChangeFactory.GetApprovedKeyPath(Entry.Source) is not null;

    /// <summary>Simplified view: the executable that runs (fallback to the raw command).</summary>
    public string FileLocationText => Entry.ExecutablePath ?? Entry.Command;

    /// <summary>Registry view: exact registry key / folder / task path plus the raw command.</summary>
    public string RegistryLocationText => $@"{Entry.SourceLocation}\{Entry.Name}";

    public string SourceLabel => Entry.Source switch
    {
        StartupSource.RegistryMachineRun => "Registry (all users)",
        StartupSource.RegistryMachineRunWow64 => "Registry (all users, 32-bit)",
        StartupSource.RegistryUserRun => "Registry (current user)",
        StartupSource.StartupFolderUser => "Startup folder (current user)",
        StartupSource.StartupFolderCommon => "Startup folder (all users)",
        StartupSource.ScheduledTask => "Scheduled task",
        _ => "Unknown",
    };

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressStaging || _disposed || !CanToggle)
            return;

        try
        {
            // Refresh baseline from the live StartupApproved state (source of truth)
            var approvedKey = StartupChangeFactory.GetApprovedKeyPath(Entry.Source)!;
            var blobResult = _registryService.ReadBinary(approvedKey, Entry.Name);
            var currentBlob = blobResult.IsSuccess ? blobResult.Value : null;
            _liveIsEnabled = currentBlob is null || currentBlob.Length == 0 || (currentBlob[0] & 1) == 0;

            var change = StartupChangeFactory.CreateToggle(Entry, value, currentBlob);
            if (change is null)
                return;

            _isStagingChange = true;
            try
            {
                if (_stagedGroupId is not null)
                {
                    _pendingChangesService.Unstage(_stagedGroupId);
                    _stagedGroupId = null;
                }

                if (value != _liveIsEnabled)
                {
                    var group = new ChangeGroup
                    {
                        GroupId = Guid.NewGuid().ToString("N"),
                        DisplayName = change.DisplayName,
                        Description = change.DisplayName,
                        Changes = [change],
                    };
                    _pendingChangesService.Stage(group);
                    _stagedGroupId = group.GroupId;
                }
            }
            finally
            {
                _isStagingChange = false;
            }

            UpdatePendingState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toggle staging failed for {Name}: {ex.Message}");
        }
    }

    private void OnPendingChangesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isStagingChange)
            return;
        if (e.PropertyName is not nameof(IPendingChangesService.PendingGroups))
            return;

        if (Dispatcher.UIThread.CheckAccess())
            HandlePendingGroupsChanged();
        else
            Dispatcher.UIThread.Post(HandlePendingGroupsChanged);
    }

    private void HandlePendingGroupsChanged()
    {
        // Our staged change was removed; either applied or discarded
        if (_stagedGroupId is not null &&
            !_pendingChangesService.PendingGroups.Any(g => g.GroupId == _stagedGroupId))
        {
            _stagedGroupId = null;

            if (_pendingChangesService.IsApplying)
            {
                // Change was applied; keep toggle position, update baseline to match
                _liveIsEnabled = IsEnabled;
            }
            else
            {
                // Change was discarded; reset toggle to live state
                _suppressStaging = true;
                IsEnabled = _liveIsEnabled;
                _suppressStaging = false;
            }

            UpdatePendingState();
        }
    }

    private void UpdatePendingState()
    {
        HasPendingChange = IsEnabled != _liveIsEnabled;
        IsPendingEnable = HasPendingChange && IsEnabled;
        IsPendingDisable = HasPendingChange && !IsEnabled;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
    }
}
