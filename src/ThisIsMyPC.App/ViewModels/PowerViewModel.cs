using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Power.Changes;
using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Power Plans module view: lists every registered plan and stages the active-plan
/// switch as a single pending change; per-plan settings load on demand into a
/// detail panel with simplified and registry display modes.
/// </summary>
public sealed partial class PowerViewModel : ObservableObject, IDisposable
{
    private readonly IPendingChangesService _pendingChangesService;
    private readonly IPowerService? _powerService;
    private readonly IRegistryService? _registryService;
    private Core.Services.IPendingActionsService? _pendingActionsService;
    private PowerPlan? _liveActivePlan;
    private string? _stagedGroupId;
    private string? _modernStandbyGroupId;
    private bool _isStagingChange;
    private bool _suppressModernStandby;

    /// <summary>The power service refuses plan switches until a restart (policy pin read at startup).</summary>
    private readonly bool _activePlanLockedByPolicy;
    private bool _disposed;

    public PowerViewModel(
        PowerScanData scanData,
        IPendingChangesService pendingChangesService,
        IPowerService? powerService = null,
        IRegistryService? registryService = null,
        Core.Services.IPendingActionsService? pendingActionsService = null)
    {
        _pendingChangesService = pendingChangesService;
        _powerService = powerService;
        _registryService = registryService;
        _pendingActionsService = pendingActionsService;
        ScanError = scanData.ScanError;
        _liveActivePlan = scanData.Plans.FirstOrDefault(p => p.IsActive);
        _activePlanLockedByPolicy = scanData.ActivePlanLockedByPolicy;

        Plans = new ObservableCollection<PowerPlanItemViewModel>(
            scanData.Plans.Select(p => new PowerPlanItemViewModel(p, pendingActionsService)));
        if (scanData.ActiveAfterRestartPlan is { } afterRestart)
        {
            foreach (var row in Plans)
                row.IsActiveAfterRestart = row.Plan.PlanGuid == afterRestart;
            RefreshSwitchBack();
        }

        if (_pendingActionsService is not null)
            _pendingActionsService.PropertyChanged += OnPendingActionsPropertyChanged;

        // Rehydrate an active-plan group staged in an earlier visit
        var existing = pendingChangesService.PendingGroups.FirstOrDefault(g =>
            g.Changes.Count == 1 &&
            g.Changes[0].ModuleId == PowerPlanChangeFactory.ModuleId &&
            g.Changes[0].SettingId == PowerPlanChangeFactory.ActivePlanSettingId);
        if (existing is not null && Guid.TryParse(existing.Changes[0].AfterValue, out var pendingGuid))
        {
            if (_liveActivePlan is not null && pendingGuid == _liveActivePlan.PlanGuid
                && !Plans.Any(p => p.IsActiveAfterRestart))
            {
                // Pending target already matches live state; drop the redundant group
                pendingChangesService.Unstage(existing.GroupId);
            }
            else
            {
                _stagedGroupId = existing.GroupId;
                SetPendingTarget(pendingGuid);
            }
        }

        InitializeModernStandby();
        _supportsModernStandby = _powerService?.SupportsModernStandby() ?? false;
        SystemPowerToggles = BuildSystemPowerRows(scanData);

        // Rehydrate plan creations staged in an earlier visit.
        foreach (var group in pendingChangesService.PendingGroups)
        {
            if (group.Changes.Count == 1
                && group.Changes[0].ModuleId == PowerPlanChangeFactory.ModuleId
                && PendingPlanCreationViewModel.IsCreation(group.Changes[0]))
            {
                PendingCreations.Add(new PendingPlanCreationViewModel(group.GroupId, group.Changes[0], pendingChangesService));
            }
        }
        PendingCreations.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPendingCreations));
            OnPropertyChanged(nameof(CanAddUltimatePerformance));
            RefreshAddPlanOptions();
        };
        Plans.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanAddUltimatePerformance));
            OnPropertyChanged(nameof(CanCreatePlans));
            RefreshAddPlanOptions();
        };
        RefreshAddPlanOptions();

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    // ---- Plan creation: a copy of an existing plan under a new name.
    //      Reversible; undo deletes the copy this app made. ----

    public ObservableCollection<PendingPlanCreationViewModel> PendingCreations { get; } = [];
    public bool HasPendingCreations => PendingCreations.Count > 0;

    /// <summary>Creating copies a plan through powrprof, so it needs the service and something to copy.</summary>
    public bool CanCreatePlans => _powerService is not null && Plans.Count > 0;

    /// <summary>
    /// Ultimate Performance can be added while no such plan exists (by
    /// marker, source GUID, or name) and none is waiting in the queue.
    /// </summary>
    public bool CanAddUltimatePerformance => _powerService is not null
        && Modules.Power.Services.PowerPlanScanner.FindUltimatePerformance(Plans.Select(p => p.Plan).ToList()) is null
        && !PendingCreations.Any(c => c.IsUltimatePerformance);

    [RelayCommand]
    private void AddUltimatePerformance()
    {
        if (!CanAddUltimatePerformance)
            return;
        StageCreation(PowerPlanChangeFactory.CreateUltimatePerformanceToggle(currentlyInstalled: false, install: true));
    }

    /// <summary>
    /// The Add plan dropdown: Windows plans missing from the list (deleted
    /// stock plans, and Ultimate Performance while absent). Rebuilt whenever
    /// the list or the queue changes.
    /// </summary>
    public ObservableCollection<AddPlanOptionViewModel> AddPlanOptions { get; } = [];
    public bool HasAddPlanOptions => AddPlanOptions.Count > 0;

    private void RefreshAddPlanOptions()
    {
        AddPlanOptions.Clear();
        if (_powerService is null)
            return;
        foreach (var stock in StockPowerPlan.All)
        {
            var present = Plans.Any(p => p.Plan.PlanGuid == stock.PlanGuid)
                || PendingCreations.Any(c => c.StockPlanGuid == stock.PlanGuid);
            if (!present)
                AddPlanOptions.Add(new AddPlanOptionViewModel(stock.Name, () => AddStockPlan(stock)));
        }
        if (CanAddUltimatePerformance)
            AddPlanOptions.Add(new AddPlanOptionViewModel("Ultimate Performance", AddUltimatePerformance));
        OnPropertyChanged(nameof(HasAddPlanOptions));
    }

    private void AddStockPlan(StockPowerPlan stock)
    {
        if (Plans.Any(p => p.Plan.PlanGuid == stock.PlanGuid) || PendingCreations.Any(c => c.StockPlanGuid == stock.PlanGuid))
            return;
        StageCreation(PowerPlanChangeFactory.CreateStockPlanRestore(stock));
    }

    private void StageCreation(ChangeDescriptor change)
    {
        var group = WrapChange(change);
        _isStagingChange = true;
        try
        {
            _pendingChangesService.Stage(group);
        }
        finally
        {
            _isStagingChange = false;
        }

        PendingCreations.Add(new PendingPlanCreationViewModel(group.GroupId, change, _pendingChangesService));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmCreatePlan))]
    private bool _isCreatingPlan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmCreatePlan))]
    [NotifyPropertyChangedFor(nameof(NewPlanNameError))]
    [NotifyPropertyChangedFor(nameof(HasNewPlanNameError))]
    private string _newPlanName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmCreatePlan))]
    private PowerPlanItemViewModel? _newPlanSource;

    /// <summary>Why the Create button is off: a name that is already taken, on the machine or in the queue.</summary>
    public string? NewPlanNameError
    {
        get
        {
            var name = NewPlanName.Trim();
            if (name.Length == 0)
                return null;
            var taken = Plans.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                || PendingCreations.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return taken ? "A plan with this name already exists." : null;
        }
    }

    public bool HasNewPlanNameError => NewPlanNameError is not null;

    public bool CanConfirmCreatePlan =>
        IsCreatingPlan && NewPlanName.Trim().Length > 0 && NewPlanNameError is null && NewPlanSource is not null;

    [RelayCommand]
    private void BeginCreatePlan()
    {
        if (!CanCreatePlans)
            return;
        NewPlanName = string.Empty;
        NewPlanSource = Plans.FirstOrDefault(p => p.IsActive) ?? Plans[0];
        IsCreatingPlan = true;
    }

    [RelayCommand]
    private void CancelCreatePlan() => IsCreatingPlan = false;

    [RelayCommand]
    private void ConfirmCreatePlan()
    {
        if (!CanConfirmCreatePlan || NewPlanSource is null)
            return;

        StageCreation(PowerPlanChangeFactory.CreatePlanChange(NewPlanName, NewPlanSource.Plan));
        IsCreatingPlan = false;
    }

    /// <summary>
    /// Pending creations the queue no longer holds were discarded or applied.
    /// After an apply the copies exist now; scan once and list them.
    /// </summary>
    private void SyncPendingCreations(bool isApplying)
    {
        var gone = PendingCreations
            .Where(c => !_pendingChangesService.PendingGroups.Any(g => g.GroupId == c.GroupId))
            .ToList();
        if (gone.Count == 0)
            return;

        foreach (var creation in gone)
            PendingCreations.Remove(creation);

        if (!isApplying || _powerService is null)
            return;

        var scanner = new Modules.Power.Services.PowerPlanScanner(_powerService);
        foreach (var plan in scanner.Scan())
        {
            if (Plans.Any(p => p.Plan.PlanGuid == plan.PlanGuid))
                continue;
            if (plan.Description != PowerPlanChangeFactory.CreatedPlanMarker
                && plan.Description != PowerPlanChangeFactory.UltimatePerformanceMarker
                && StockPowerPlan.FindByGuid(plan.PlanGuid) is null)
                continue;
            Plans.Add(new PowerPlanItemViewModel(plan, _pendingActionsService));
        }
        ResortPlans();
        OnPropertyChanged(nameof(HasNoPlans));
        OnPropertyChanged(nameof(NewPlanNameError));
    }

    public ObservableCollection<PowerPlanItemViewModel> Plans { get; }

    /// <summary>
    /// Puts the rows back in list order from their live state (the active
    /// flag moves after an applied switch; a restored plan lands at the end).
    /// Moves, never rebuilds, so the cards keep their bindings.
    /// </summary>
    private void ResortPlans()
    {
        var ordered = Plans
            .OrderBy(p => Modules.Power.Services.PowerPlanOrder.Rank(p.Plan, p.IsActive))
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var current = Plans.IndexOf(ordered[i]);
            if (current != i)
                Plans.Move(current, i);
        }
    }

    public string? ScanError { get; }
    public bool HasScanError => !string.IsNullOrEmpty(ScanError);
    public bool HasNoPlans => Plans.Count == 0 && !HasScanError;

    /// <summary>Staging needs a live active plan as the before-state.</summary>
    public bool CanSelectPlans => _liveActivePlan is not null;

    public bool CanOpenSettings => _powerService is not null;

    // ---- Settings detail panel ----

    [ObservableProperty]
    private bool _isSettingsView;

    [ObservableProperty]
    private PowerPlanItemViewModel? _settingsPlan;

    [ObservableProperty]
    private bool _isRegistryView;

    [ObservableProperty]
    private bool _isLoadingSettings;

    [ObservableProperty]
    private string? _settingsError;

    public bool HasSettingsError => !string.IsNullOrEmpty(SettingsError);

    public ObservableCollection<PowerSettingGroupViewModel> SettingsGroups { get; } = [];

    // ---- Modern Standby ----

    [ObservableProperty]
    private bool _showModernStandby;

    [ObservableProperty]
    private bool _isModernStandbyDisabled;

    public string ModernStandbyDescription =>
        "Modern Standby (S0 low-power idle) keeps the machine partially awake while sleeping so it can " +
        "sync in the background. Disabling it makes Windows use classic S3 sleep at the next boot " +
        "(only if the firmware still supports S3). Applied via the PlatformAoAcOverride registry value.";

    // ---- System power: hibernation + Ultimate Performance (ShellSettingViewModel rows) ----

    public IReadOnlyList<ShellSettingViewModel> SystemPowerToggles { get; }

    public bool ShowSystemPower => SystemPowerToggles.Count > 0;

    private readonly bool _supportsModernStandby;

    /// <summary>On Modern Standby machines the classic plan system is largely bypassed.</summary>
    public bool ShowUltimatePerformanceCaveat => _supportsModernStandby;

    public string UltimatePerformanceCaveat =>
        "This PC uses Modern Standby. Windows may largely ignore classic power plans here.";

    private const string HibernateDescription =
        "Hibernation writes memory to disk so the machine can power off fully and resume. " +
        "Disabling it deletes the hiberfile, which also turns off Fast Startup and removes " +
        "Hibernate from the power menu. On laptops it also removes the critical-battery " +
        "hibernate safety net and any hibernate-instead-of-sleep timers.";

    /// <summary>Tooltip on the Add Ultimate Performance plan button.</summary>
    public string UltimatePerformanceDescription =>
        "Adds the hidden Ultimate Performance plan Windows ships for workstations. " +
        "It removes micro-latencies at the cost of higher idle power use. Undo deletes " +
        "the copy this app made; the plan must not be active then.";

    private List<ShellSettingViewModel> BuildSystemPowerRows(PowerScanData scanData)
    {
        var rows = new List<ShellSettingViewModel>();

        if (_registryService is not null && scanData.HibernateEnabled is { } hibernateAtScan)
        {
            // lastKnown is the most recently OBSERVED state (scan or later read);
            // a failed read never fabricates a before-state.
            var lastKnown = hibernateAtScan;
            bool ReadHibernate()
            {
                var read = _registryService.ReadDWord(
                    PowerPlanChangeFactory.ModernStandbyKeyPath, PowerPlanChangeFactory.HibernateValueName);
                if (read is { IsSuccess: true })
                    lastKnown = read.Value != 0;
                return lastKnown;
            }

            rows.Add(new ShellSettingViewModel(
                "Allow hibernation",
                HibernateDescription,
                "powrprof:SystemReserveHiberFile",
                hibernateAtScan,
                _pendingChangesService,
                groupFactory: desired => WrapChange(
                    PowerPlanChangeFactory.CreateHibernateToggle(lastKnown, desired)),
                readRegistryState: ReadHibernate,
                rehydrateSettingId: PowerPlanChangeFactory.HibernateSettingId));
        }

        if (_registryService is not null && scanData.PolicyPinnedPlan is { } pin)
        {
            var pinnedName = scanData.Plans.FirstOrDefault(p => p.PlanGuid == pin)?.Name ?? pin.ToString("D");
            bool ReadPin() => _registryService.ReadString(
                PowerPlanChangeFactory.ActivePlanPolicyKeyPath, PowerPlanChangeFactory.ActivePlanPolicyValueName)
                is { IsSuccess: true, Value.Length: > 0 };

            rows.Add(new ShellSettingViewModel(
                "Pin the active plan by policy",
                $"A Group Policy value pins '{pinnedName}'; tools such as winutil set it. " +
                "Windows applies the pinned plan at startup and refuses plan switches until a restart.",
                PowerPlanChangeFactory.ActivePlanPolicyKeyPath + "\\" + PowerPlanChangeFactory.ActivePlanPolicyValueName,
                true,
                _pendingChangesService,
                groupFactory: keep => WrapChange(PowerPlanChangeFactory.CreatePolicyPinToggle(pin, keep)),
                readRegistryState: ReadPin,
                rehydrateSettingId: PowerPlanChangeFactory.ActivePlanPolicyPinSettingId));
        }

        return rows;
    }

    private static ChangeGroup WrapChange(ChangeDescriptor change) => new()
    {
        GroupId = Guid.NewGuid().ToString("N"),
        DisplayName = change.DisplayName,
        Description = change.DisplayName,
        Changes = [change],
    };

    [RelayCommand]
    private void SelectPlan(PowerPlanItemViewModel? plan)
    {
        if (plan is null || _disposed || _liveActivePlan is null)
            return;

        _isStagingChange = true;
        try
        {
            if (_stagedGroupId is not null)
            {
                _pendingChangesService.Unstage(_stagedGroupId);
                _stagedGroupId = null;
            }

            if (plan.Plan.PlanGuid != _liveActivePlan.PlanGuid)
            {
                var change = PowerPlanChangeFactory.CreateActivePlanChange(_liveActivePlan, plan.Plan, _activePlanLockedByPolicy);
                Stage(change, out _stagedGroupId);
            }
            else if (Plans.FirstOrDefault(p => p.IsActiveAfterRestart) is { } restartPlan)
            {
                // Windows would switch at the next startup; the way back is a
                // switch to the live plan, which the power service accepts at
                // once, so no restart is involved.
                var change = PowerPlanChangeFactory.CreateActivePlanChange(restartPlan.Plan, _liveActivePlan);
                Stage(change, out _stagedGroupId);
            }
            // Otherwise selecting the live active plan just clears the pending switch
        }
        finally
        {
            _isStagingChange = false;
        }

        SetPendingTarget(_stagedGroupId is not null ? plan.Plan.PlanGuid : null);
    }

    /// <summary>Discards the staged plan switch without touching other pending changes.</summary>
    [RelayCommand]
    private void CancelPendingSwitch()
    {
        if (_disposed || _stagedGroupId is null)
            return;

        _isStagingChange = true;
        try
        {
            _pendingChangesService.Unstage(_stagedGroupId);
            _stagedGroupId = null;
        }
        finally
        {
            _isStagingChange = false;
        }

        SetPendingTarget(null);
    }

    private int _settingsLoadGeneration;

    [RelayCommand]
    private async Task OpenSettingsAsync(PowerPlanItemViewModel? plan)
    {
        if (plan is null || _disposed || _powerService is null || IsLoadingSettings)
            return;

        SettingsPlan = plan;
        IsSettingsView = true;
        IsLoadingSettings = true;
        SettingsError = null;
        DisposeSettingRows();
        var generation = ++_settingsLoadGeneration;

        try
        {
            var powerService = _powerService;
            var result = await Task.Run(() => powerService.EnumeratePlanSettings(plan.Plan.PlanGuid));
            // Back was clicked (or a newer load started) while enumerating; discard
            if (_disposed || generation != _settingsLoadGeneration)
                return;

            if (!result.IsSuccess || result.Value is null)
            {
                SettingsError = result.ErrorMessage ?? "Failed to load power plan settings.";
            }
            else
            {
                foreach (var subgroup in result.Value.GroupBy(s => s.SubgroupGuid))
                {
                    var rows = subgroup
                        .Select(info => new PowerSettingItemViewModel(
                            plan.Plan, PowerSetting.FromInfo(info), _pendingChangesService))
                        .ToList();
                    SettingsGroups.Add(new PowerSettingGroupViewModel(rows[0].Setting.SubgroupName, rows));
                }
            }
        }
        catch (Exception ex)
        {
            SettingsError = $"Failed to load power plan settings: {ex.Message}";
        }
        finally
        {
            IsLoadingSettings = false;
            OnPropertyChanged(nameof(HasSettingsError));
        }
    }

    [RelayCommand]
    private void CloseSettings()
    {
        _settingsLoadGeneration++;
        IsSettingsView = false;
        SettingsPlan = null;
        SettingsError = null;
        OnPropertyChanged(nameof(HasSettingsError));
        DisposeSettingRows();
    }

    [RelayCommand]
    private void ToggleModernStandby()
    {
        if (_disposed || _registryService is null || _suppressModernStandby)
            return;

        var disable = IsModernStandbyDisabled;
        _isStagingChange = true;
        try
        {
            if (_modernStandbyGroupId is not null)
            {
                _pendingChangesService.Unstage(_modernStandbyGroupId);
                _modernStandbyGroupId = null;
            }

            var current = ReadModernStandbyOverride();
            var liveDisabled = current == 0;
            if (disable != liveDisabled)
            {
                var change = PowerPlanChangeFactory.CreateModernStandbyToggle(current, disable);
                Stage(change, out _modernStandbyGroupId);
            }
        }
        finally
        {
            _isStagingChange = false;
        }
    }

    private void InitializeModernStandby()
    {
        var current = ReadModernStandbyOverride();
        ShowModernStandby = _registryService is not null &&
            (_supportsModernStandby || current is not null);
        _suppressModernStandby = true;
        IsModernStandbyDisabled = current == 0;
        _suppressModernStandby = false;

        // Rehydrate a Modern Standby toggle staged in an earlier visit
        var existing = _pendingChangesService.PendingGroups.FirstOrDefault(g =>
            g.Changes.Count == 1 &&
            g.Changes[0].ModuleId == PowerPlanChangeFactory.ModuleId &&
            g.Changes[0].SettingId == PowerPlanChangeFactory.ModernStandbySettingId);
        if (existing is null)
            return;

        var pendingDisabled = existing.Changes[0].AfterValue == "0";
        if (pendingDisabled == IsModernStandbyDisabled)
        {
            _pendingChangesService.Unstage(existing.GroupId);
        }
        else
        {
            _modernStandbyGroupId = existing.GroupId;
            _suppressModernStandby = true;
            IsModernStandbyDisabled = pendingDisabled;
            _suppressModernStandby = false;
        }
    }

    private int? ReadModernStandbyOverride()
    {
        var read = _registryService?.ReadDWord(
            PowerPlanChangeFactory.ModernStandbyKeyPath, PowerPlanChangeFactory.ModernStandbyValueName);
        return read is { IsSuccess: true } ? read.Value : null;
    }

    private void Stage(ChangeDescriptor change, out string? groupId)
    {
        var group = new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = change.DisplayName,
            Description = change.DisplayName,
            Changes = [change],
        };
        _pendingChangesService.Stage(group);
        groupId = group.GroupId;
    }

    /// <summary>The live plan offers "Keep active" while another plan waits for the restart.</summary>
    private void RefreshSwitchBack()
    {
        var waiting = Plans.Any(p => p.IsActiveAfterRestart);
        foreach (var row in Plans)
            row.CanSwitchBack = waiting && row.IsActive;
    }

    private void SetPendingTarget(Guid? targetGuid)
    {
        foreach (var row in Plans)
            row.IsPendingTarget = targetGuid is { } guid && row.Plan.PlanGuid == guid;
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
        var isApplying = _pendingChangesService.IsApplying;
        SyncPendingCreations(isApplying);

        // Active-plan switch removed externally (review-panel discard or apply)
        if (_stagedGroupId is not null &&
            !_pendingChangesService.PendingGroups.Any(g => g.GroupId == _stagedGroupId))
        {
            _stagedGroupId = null;
            var pendingTarget = Plans.FirstOrDefault(p => p.IsPendingTarget);

            if (isApplying && pendingTarget is not null && _activePlanLockedByPolicy)
            {
                // Applied, but the power service holds its startup pin: Windows
                // switches at the next restart, and the live plan stays active.
                // A switch back to the live plan clears the wait instead.
                foreach (var row in Plans)
                    row.IsActiveAfterRestart = row.Plan.PlanGuid == pendingTarget.Plan.PlanGuid && !row.IsActive;
            }
            else if (isApplying && pendingTarget is not null)
            {
                // The switch was applied; the pending target is now the live active plan
                _liveActivePlan = pendingTarget.Plan;
                foreach (var row in Plans)
                {
                    row.IsActive = row.Plan.PlanGuid == pendingTarget.Plan.PlanGuid;
                    row.IsActiveAfterRestart = false;
                }
                ResortPlans();
            }

            SetPendingTarget(null);
            RefreshSwitchBack();
        }

        // Modern Standby toggle removed externally
        if (_modernStandbyGroupId is not null &&
            !_pendingChangesService.PendingGroups.Any(g => g.GroupId == _modernStandbyGroupId))
        {
            _modernStandbyGroupId = null;
            _suppressModernStandby = true;
            IsModernStandbyDisabled = isApplying
                ? IsModernStandbyDisabled // applied; the toggle already shows the new state
                : ReadModernStandbyOverride() == 0;
            _suppressModernStandby = false;
        }

        // Setting rows track their own staged groups; refresh them from the parent
        // so the rows need no per-row event subscriptions.
        foreach (var group in SettingsGroups)
        {
            foreach (var row in group.Settings)
                row.OnPendingGroupsChanged(_pendingChangesService, isApplying);
        }
    }

    private void DisposeSettingRows()
    {
        SettingsGroups.Clear();
    }

    private void OnPendingActionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Apply/discard empties the queue outside this view; rows must drop
        // their queued state.
        if (e.PropertyName is not nameof(Core.Services.IPendingActionsService.PendingActions))
            return;

        if (Dispatcher.UIThread.CheckAccess())
            RefreshDeleteQueuedStates();
        else
            Dispatcher.UIThread.Post(RefreshDeleteQueuedStates);
    }

    private void RefreshDeleteQueuedStates()
    {
        foreach (var plan in Plans)
            plan.RefreshQueuedState();
    }

    /// <summary>Drops rows whose delete action just succeeded. Called by the host after Apply.</summary>
    public void ApplyActionResults(Core.Actions.ActionBatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        foreach (var action in result.Succeeded)
        {
            var row = Plans.FirstOrDefault(p => p.DeleteActionId == action.ActionId);
            if (row is not null)
                Plans.Remove(row);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var row in SystemPowerToggles)
            row.Dispose();
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
        if (_pendingActionsService is not null)
            _pendingActionsService.PropertyChanged -= OnPendingActionsPropertyChanged;
    }
}

/// <summary>One entry of the Add plan dropdown.</summary>
public sealed partial class AddPlanOptionViewModel(string name, Action add) : ObservableObject
{
    public string Name { get; } = name;

    [RelayCommand]
    private void Add() => add();
}

/// <summary>A plan creation waiting in the queue: shown under the plan list until Apply or Remove.</summary>
public sealed partial class PendingPlanCreationViewModel : ObservableObject
{
    private readonly IPendingChangesService _pendingChangesService;

    public PendingPlanCreationViewModel(string groupId, ChangeDescriptor change, IPendingChangesService pendingChangesService)
    {
        ArgumentNullException.ThrowIfNull(change);
        GroupId = groupId;
        IsUltimatePerformance = change.SettingId == PowerPlanChangeFactory.UltimatePerformanceSettingId;
        if (change.SettingId.StartsWith(PowerPlanChangeFactory.AddStockPlanPrefix, StringComparison.Ordinal)
            && Guid.TryParse(change.SettingId[PowerPlanChangeFactory.AddStockPlanPrefix.Length..], out var stockGuid))
        {
            StockPlanGuid = stockGuid;
        }
        var stock = StockPlanGuid is { } g ? StockPowerPlan.FindByGuid(g) : null;
        Name = IsUltimatePerformance
            ? "Ultimate Performance"
            : stock?.Name ?? (StockPlanGuid is null ? change.SettingId[PowerPlanChangeFactory.CreatePlanPrefix.Length..] : change.DisplayName);
        Detail = IsUltimatePerformance
            ? "Hidden Windows plan for workstations"
            : stock is not null
                ? "Windows default plan"
                : change.AfterDisplay;
        _pendingChangesService = pendingChangesService;
    }

    /// <summary>Set for a deleted stock plan being put back; the option list hides that plan meanwhile.</summary>
    public Guid? StockPlanGuid { get; }

    /// <summary>A staged change this row can stand for: a named copy, or the Ultimate Performance install.</summary>
    public static bool IsCreation(ChangeDescriptor change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return change.SettingId.StartsWith(PowerPlanChangeFactory.CreatePlanPrefix, StringComparison.Ordinal)
            || change.SettingId.StartsWith(PowerPlanChangeFactory.AddStockPlanPrefix, StringComparison.Ordinal)
            || (change.SettingId == PowerPlanChangeFactory.UltimatePerformanceSettingId && change.AfterValue == "1");
    }

    public string GroupId { get; }
    public string Name { get; }
    public string Detail { get; }
    public bool IsUltimatePerformance { get; }

    [RelayCommand]
    private void Remove() => _pendingChangesService.Unstage(GroupId);
}

public sealed partial class PowerPlanItemViewModel : ObservableObject
{
    private readonly Core.Services.IPendingActionsService? _pendingActionsService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(ShowSetActive))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSetActive))]
    private bool _isPendingTarget;

    /// <summary>Windows activates this plan at the next startup; the live plan stays active until then.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSetActive))]
    private bool _isActiveAfterRestart;

    /// <summary>The live plan while another plan waits for the restart: "Keep active" stages the way back.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSetActive))]
    [NotifyPropertyChangedFor(nameof(SetActiveLabel))]
    private bool _canSwitchBack;

    /// <summary>
    /// A pending target has Cancel instead, and a plan already recorded for
    /// the restart has nothing left to stage; otherwise every plan but the
    /// live one, and the live one while another waits for the restart.
    /// </summary>
    public bool ShowSetActive => !IsPendingTarget && !IsActiveAfterRestart && (!IsActive || CanSwitchBack);

    public string SetActiveLabel => CanSwitchBack ? "Keep active" : "Set active";

    public PowerPlanItemViewModel(PowerPlan plan, Core.Services.IPendingActionsService? pendingActionsService = null)
    {
        Plan = plan;
        _isActive = plan.IsActive;
        _pendingActionsService = pendingActionsService;
        _isDeleteQueued = pendingActionsService?.IsStaged(DeleteActionId) ?? false;
    }

    public PowerPlan Plan { get; }

    public string Name => Plan.Name;
    public string DescriptionText => Plan.Description ?? string.Empty;
    public bool HasDescription => !string.IsNullOrEmpty(Plan.Description);
    public bool IsNormallyHidden => Plan.IsNormallyHidden;
    public string HiddenNote => "Normally hidden by Windows; not shown in Control Panel on most editions";
    public string GuidText => Plan.PlanGuid.ToString("D");

    // ---- One-way deletion (debloating the plan zoo) ----

    internal string DeleteActionId =>
        Modules.Power.Actions.PowerActionFactory.DeletePlanPrefix + Plan.PlanGuid.ToString("D");

    public bool CanDelete => !IsActive && _pendingActionsService is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteButtonText))]
    private bool _isDeleteQueued;

    public string DeleteButtonText => IsDeleteQueued ? "Queued" : "Delete";

    [RelayCommand]
    private void ToggleDeleteQueue()
    {
        if (_pendingActionsService is null || IsActive)
            return;

        if (IsDeleteQueued)
            _pendingActionsService.Unstage(DeleteActionId);
        else
            _pendingActionsService.Stage(Modules.Power.Actions.PowerActionFactory.CreateDeletePlan(Plan));

        RefreshQueuedState();
    }

    public void RefreshQueuedState() =>
        IsDeleteQueued = _pendingActionsService?.IsStaged(DeleteActionId) ?? false;
}

public sealed class PowerSettingGroupViewModel
{
    public PowerSettingGroupViewModel(string groupName, IReadOnlyList<PowerSettingItemViewModel> settings)
    {
        GroupName = groupName;
        Settings = settings;
    }

    public string GroupName { get; }
    public IReadOnlyList<PowerSettingItemViewModel> Settings { get; }

    /// <summary>Tab header: the group and how many settings it holds.</summary>
    public string Header => $"{GroupName} ({Settings.Count})";
}

/// <summary>
/// One setting row with independent AC ("Plugged in") and DC ("On battery")
/// editors. Enumerated settings edit via ComboBox option; range settings via a
/// validated numeric TextBox. Each scope stages its own single-change group.
/// </summary>
public sealed partial class PowerSettingItemViewModel : ObservableObject
{
    private readonly IPendingChangesService _pendingChangesService;
    private readonly PowerPlan _plan;
    private uint? _liveAc;
    private uint? _liveDc;
    private string? _acGroupId;
    private string? _dcGroupId;
    private bool _suppressStaging;

    public PowerSettingItemViewModel(PowerPlan plan, PowerSetting setting, IPendingChangesService pendingChangesService)
    {
        _plan = plan;
        Setting = setting;
        _pendingChangesService = pendingChangesService;
        _liveAc = setting.AcIndex;
        _liveDc = setting.DcIndex;

        Options = setting.PossibleValues.Select(v => v.Name).ToList();

        _suppressStaging = true;
        _selectedAcOptionIndex = PositionOf(_liveAc);
        _selectedDcOptionIndex = PositionOf(_liveDc);
        _acText = _liveAc?.ToString() ?? string.Empty;
        _dcText = _liveDc?.ToString() ?? string.Empty;

        // Rehydrate groups staged in an earlier visit (one per scope)
        Rehydrate(ac: true, ref _acGroupId);
        Rehydrate(ac: false, ref _dcGroupId);
        _suppressStaging = false;
        UpdatePendingFlags();
    }

    public PowerSetting Setting { get; }

    public string Name => Setting.Name;
    public string DescriptionText => Setting.Description ?? string.Empty;
    public bool HasDescription => !string.IsNullOrEmpty(Setting.Description);

    public bool IsEnumerated => !Setting.IsRange && Setting.PossibleValues.Count > 0;
    public bool IsRangeEditor => !IsEnumerated;
    public bool CanEditAc => _liveAc is not null;
    public bool CanEditDc => _liveDc is not null;

    public IReadOnlyList<string> Options { get; } = [];

    public string RangeHint
    {
        get
        {
            if (!Setting.IsRange)
                return string.Empty;
            var units = string.IsNullOrEmpty(Setting.Units) ? string.Empty : $" {Setting.Units}";
            return $"{Setting.Min}–{Setting.Max}{units}";
        }
    }

    // Registry view (AC3): raw GUIDs and value indexes
    public string SettingGuidText => Setting.SettingGuid.ToString("D");
    public string SubgroupGuidText => Setting.SubgroupGuid.ToString("D");
    public string RawValuesText =>
        $"AC={(_liveAc?.ToString() ?? "?")}  DC={(_liveDc?.ToString() ?? "?")}";

    // Enumerated editors bind by POSITION in Options/PossibleValues, not display
    // string; duplicate friendly names would otherwise stage the wrong index.
    [ObservableProperty]
    private int _selectedAcOptionIndex = -1;

    [ObservableProperty]
    private int _selectedDcOptionIndex = -1;

    [ObservableProperty]
    private string _acText;

    [ObservableProperty]
    private string _dcText;

    [ObservableProperty]
    private bool _hasPendingAc;

    [ObservableProperty]
    private bool _hasPendingDc;

    private string SettingId(bool ac) =>
        $"{PowerPlanChangeFactory.SettingIdPrefix}{_plan.PlanGuid:D}:{Setting.SettingGuid:D}:{(ac ? "AC" : "DC")}";

    /// <summary>Position in PossibleValues/Options for a value index; -1 when absent.</summary>
    private int PositionOf(uint? valueIndex)
    {
        if (valueIndex is not { } target)
            return -1;
        for (var i = 0; i < Setting.PossibleValues.Count; i++)
        {
            if (Setting.PossibleValues[i].Index == target)
                return i;
        }
        return -1;
    }

    private uint? ValueAtPosition(int position) =>
        position >= 0 && position < Setting.PossibleValues.Count
            ? Setting.PossibleValues[position].Index
            : null;

    partial void OnSelectedAcOptionIndexChanged(int value) => StageEnumerated(ac: true, value);
    partial void OnSelectedDcOptionIndexChanged(int value) => StageEnumerated(ac: false, value);
    partial void OnAcTextChanged(string value) => StageRange(ac: true, value);
    partial void OnDcTextChanged(string value) => StageRange(ac: false, value);

    private void StageEnumerated(bool ac, int position)
    {
        if (_suppressStaging || !IsEnumerated)
            return;
        if (ValueAtPosition(position) is { } desired)
            StageScope(ac, desired);
    }

    private void StageRange(bool ac, string text)
    {
        if (_suppressStaging || IsEnumerated)
            return;
        if (!uint.TryParse(text, out var desired))
            return; // invalid intermediate input; leave the last staged value alone
        if (Setting.IsRange && (desired < Setting.Min || desired > Setting.Max))
            return;
        StageScope(ac, desired);
    }

    private void StageScope(bool ac, uint desired)
    {
        var live = ac ? _liveAc : _liveDc;
        if (live is null)
            return;

        ref var groupId = ref ac ? ref _acGroupId : ref _dcGroupId;
        if (groupId is not null)
        {
            // Clear the field BEFORE unstaging: Unstage raises PropertyChanged
            // synchronously, which re-enters ReconcileScope via the parent; a
            // still-set field would read as "discarded externally" and snap the
            // editor back to the live value mid-edit.
            var staleGroupId = groupId;
            groupId = null;
            _pendingChangesService.Unstage(staleGroupId);
        }

        if (desired != live.Value)
        {
            var change = PowerPlanChangeFactory.CreateSettingChange(_plan, Setting, ac, live.Value, desired);
            var group = new ChangeGroup
            {
                GroupId = Guid.NewGuid().ToString("N"),
                DisplayName = change.DisplayName,
                Description = change.DisplayName,
                Changes = [change],
            };
            _pendingChangesService.Stage(group);
            groupId = group.GroupId;
        }

        UpdatePendingFlags();
    }

    private void Rehydrate(bool ac, ref string? groupId)
    {
        var settingId = SettingId(ac);
        var existing = _pendingChangesService.PendingGroups.FirstOrDefault(g =>
            g.Changes.Count == 1 &&
            g.Changes[0].ModuleId == PowerPlanChangeFactory.ModuleId &&
            g.Changes[0].SettingId == settingId);
        if (existing is null || !uint.TryParse(existing.Changes[0].AfterValue, out var pendingIndex))
            return;

        var live = ac ? _liveAc : _liveDc;
        if (pendingIndex == live)
        {
            _pendingChangesService.Unstage(existing.GroupId);
            return;
        }

        groupId = existing.GroupId;
        if (ac)
        {
            SelectedAcOptionIndex = PositionOf(pendingIndex);
            AcText = pendingIndex.ToString();
        }
        else
        {
            SelectedDcOptionIndex = PositionOf(pendingIndex);
            DcText = pendingIndex.ToString();
        }
    }

    /// <summary>Called by the parent when PendingGroups changed externally (apply or review-panel discard).</summary>
    public void OnPendingGroupsChanged(IPendingChangesService pending, bool isApplying)
    {
        ReconcileScope(pending, isApplying, ac: true, ref _acGroupId);
        ReconcileScope(pending, isApplying, ac: false, ref _dcGroupId);
        UpdatePendingFlags();
    }

    private void ReconcileScope(IPendingChangesService pending, bool isApplying, bool ac, ref string? groupId)
    {
        var currentGroupId = groupId;
        if (currentGroupId is null || pending.PendingGroups.Any(g => g.GroupId == currentGroupId))
            return;
        groupId = null;

        if (isApplying)
        {
            // Applied; the editor value is now the live value
            var text = ac ? AcText : DcText;
            var position = ac ? SelectedAcOptionIndex : SelectedDcOptionIndex;
            var applied = IsEnumerated ? ValueAtPosition(position) : uint.TryParse(text, out var v) ? v : null;
            if (applied is not null)
            {
                if (ac) _liveAc = applied; else _liveDc = applied;
                OnPropertyChanged(nameof(RawValuesText));
            }
        }
        else
        {
            // Discarded; revert the editor to live state
            _suppressStaging = true;
            if (ac)
            {
                SelectedAcOptionIndex = PositionOf(_liveAc);
                AcText = _liveAc?.ToString() ?? string.Empty;
            }
            else
            {
                SelectedDcOptionIndex = PositionOf(_liveDc);
                DcText = _liveDc?.ToString() ?? string.Empty;
            }
            _suppressStaging = false;
        }
    }

    private void UpdatePendingFlags()
    {
        HasPendingAc = _acGroupId is not null;
        HasPendingDc = _dcGroupId is not null;
    }
}
