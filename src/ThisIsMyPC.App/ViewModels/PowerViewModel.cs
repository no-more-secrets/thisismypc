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
    private PowerPlan? _liveActivePlan;
    private string? _stagedGroupId;
    private string? _modernStandbyGroupId;
    private bool _isStagingChange;
    private bool _suppressModernStandby;
    private bool _disposed;

    public PowerViewModel(
        PowerScanData scanData,
        IPendingChangesService pendingChangesService,
        IPowerService? powerService = null,
        IRegistryService? registryService = null)
    {
        _pendingChangesService = pendingChangesService;
        _powerService = powerService;
        _registryService = registryService;
        ScanError = scanData.ScanError;
        _liveActivePlan = scanData.Plans.FirstOrDefault(p => p.IsActive);

        Plans = new ObservableCollection<PowerPlanItemViewModel>(
            scanData.Plans.Select(p => new PowerPlanItemViewModel(p)));

        // Rehydrate an active-plan group staged in an earlier visit
        var existing = pendingChangesService.PendingGroups.FirstOrDefault(g =>
            g.Changes.Count == 1 &&
            g.Changes[0].ModuleId == PowerPlanChangeFactory.ModuleId &&
            g.Changes[0].SettingId == PowerPlanChangeFactory.ActivePlanSettingId);
        if (existing is not null && Guid.TryParse(existing.Changes[0].AfterValue, out var pendingGuid))
        {
            if (_liveActivePlan is not null && pendingGuid == _liveActivePlan.PlanGuid)
            {
                // Pending target already matches live state — drop the redundant group
                pendingChangesService.Unstage(existing.GroupId);
            }
            else
            {
                _stagedGroupId = existing.GroupId;
                SetPendingTarget(pendingGuid);
            }
        }

        InitializeModernStandby();

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    public ObservableCollection<PowerPlanItemViewModel> Plans { get; }

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

            // Selecting the live active plan just clears the pending switch
            if (plan.Plan.PlanGuid != _liveActivePlan.PlanGuid)
            {
                var change = PowerPlanChangeFactory.CreateActivePlanChange(_liveActivePlan, plan.Plan);
                Stage(change, out _stagedGroupId);
            }
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
            // Back was clicked (or a newer load started) while enumerating — discard
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
            ((_powerService?.SupportsModernStandby() ?? false) || current is not null);
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

        // Active-plan switch removed externally (review-panel discard or apply)
        if (_stagedGroupId is not null &&
            !_pendingChangesService.PendingGroups.Any(g => g.GroupId == _stagedGroupId))
        {
            _stagedGroupId = null;
            var pendingTarget = Plans.FirstOrDefault(p => p.IsPendingTarget);

            if (isApplying && pendingTarget is not null)
            {
                // The switch was applied — the pending target is now the live active plan
                _liveActivePlan = pendingTarget.Plan;
                foreach (var row in Plans)
                    row.IsActive = row.Plan.PlanGuid == pendingTarget.Plan.PlanGuid;
            }

            SetPendingTarget(null);
        }

        // Modern Standby toggle removed externally
        if (_modernStandbyGroupId is not null &&
            !_pendingChangesService.PendingGroups.Any(g => g.GroupId == _modernStandbyGroupId))
        {
            _modernStandbyGroupId = null;
            _suppressModernStandby = true;
            IsModernStandbyDisabled = isApplying
                ? IsModernStandbyDisabled // applied — the toggle already shows the new state
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
    }
}

public sealed partial class PowerPlanItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isPendingTarget;

    public PowerPlanItemViewModel(PowerPlan plan)
    {
        Plan = plan;
        _isActive = plan.IsActive;
    }

    public PowerPlan Plan { get; }

    public string Name => Plan.Name;
    public string DescriptionText => Plan.Description ?? string.Empty;
    public bool HasDescription => !string.IsNullOrEmpty(Plan.Description);
    public bool IsNormallyHidden => Plan.IsNormallyHidden;
    public string HiddenNote => "Normally hidden by Windows — not shown in Control Panel on most editions";
    public string GuidText => Plan.PlanGuid.ToString("D");
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
    // string — duplicate friendly names would otherwise stage the wrong index.
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
            return; // invalid intermediate input — leave the last staged value alone
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
            // synchronously, which re-enters ReconcileScope via the parent — a
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
            // Applied — the editor value is now the live value
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
            // Discarded — revert the editor to live state
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
