using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
/// switch as a single pending change (the active plan is one logical setting).
/// </summary>
public sealed partial class PowerViewModel : ObservableObject, IDisposable
{
    private readonly IPendingChangesService _pendingChangesService;
    private PowerPlan? _liveActivePlan;
    private string? _stagedGroupId;
    private bool _isStagingChange;
    private bool _disposed;

    public PowerViewModel(PowerScanData scanData, IPendingChangesService pendingChangesService)
    {
        _pendingChangesService = pendingChangesService;
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

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    public ObservableCollection<PowerPlanItemViewModel> Plans { get; }

    public string? ScanError { get; }
    public bool HasScanError => !string.IsNullOrEmpty(ScanError);
    public bool HasNoPlans => Plans.Count == 0 && !HasScanError;

    /// <summary>Staging needs a live active plan as the before-state.</summary>
    public bool CanSelectPlans => _liveActivePlan is not null;

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
        if (_stagedGroupId is null ||
            _pendingChangesService.PendingGroups.Any(g => g.GroupId == _stagedGroupId))
        {
            return;
        }

        _stagedGroupId = null;
        var pendingTarget = Plans.FirstOrDefault(p => p.IsPendingTarget);

        if (_pendingChangesService.IsApplying && pendingTarget is not null)
        {
            // The switch was applied — the pending target is now the live active plan
            _liveActivePlan = pendingTarget.Plan;
            foreach (var row in Plans)
                row.IsActive = row.Plan.PlanGuid == pendingTarget.Plan.PlanGuid;
        }

        SetPendingTarget(null);
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
