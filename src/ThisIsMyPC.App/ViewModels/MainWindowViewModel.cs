using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly NavigationService _navigationService;
    private readonly IPendingChangesService _pendingChangesService;
    private readonly IChangeHistoryService _changeHistoryService;

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
    [NotifyPropertyChangedFor(nameof(CanModifyPending))]
    private int _pendingCount;

    public bool HasPendingChanges => PendingCount > 0;

    public string PendingCountText => PendingCount == 0
        ? "No pending changes"
        : $"{PendingCount} change{(PendingCount == 1 ? "" : "s")} pending";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanModifyPending))]
    private bool _isApplying;

    public bool CanModifyPending => HasPendingChanges && !IsApplying;

    public ReviewPanelViewModel ReviewPanel { get; }

    public ChangeHistoryViewModel ChangeHistory { get; }

    [ObservableProperty]
    private bool _isHistoryPanelOpen;

    public MainWindowViewModel(
        NavigationService navigationService,
        IPendingChangesService pendingChangesService,
        IChangeHistoryService changeHistoryService,
        ReviewPanelViewModel reviewPanel)
    {
        _navigationService = navigationService;
        _pendingChangesService = pendingChangesService;
        _changeHistoryService = changeHistoryService;
        ReviewPanel = reviewPanel;
        ChangeHistory = new ChangeHistoryViewModel(
            changeHistoryService,
            RevertChangeOnModule,
            ApplyChangeToModule);

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
        _navigationService.PropertyChanged += OnNavigationPropertyChanged;
        PendingCount = _pendingChangesService.PendingCount;
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

        try
        {
            var current = _navigationService.CurrentModule;
            if (current?.Module is ShellModule)
            {
                var scanResult = await current.Module.ScanSystemStateAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (scanResult.IsSuccess && scanResult.Value is ShellScanData scanData)
                    {
                        CurrentContent = new ShellViewModel(scanData, _pendingChangesService);
                    }
                    else
                    {
                        CurrentContent = null;
                        StatusMessage = scanResult.ErrorMessage ?? "Failed to scan shell settings";
                    }
                });
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() => CurrentContent = null);
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                StatusMessage = $"Failed to load module: {ex.Message}");
        }
    }

    public async Task InitializeAsync()
    {
        await _changeHistoryService.InitializeAsync().ConfigureAwait(true);
        await _navigationService.InitializeAsync().ConfigureAwait(true);

        PopulateSidebar();

        _navigationService.NavigateToFirstAvailable();
        SyncSelectedModule();
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

    [RelayCommand]
    private void NavigateToModule(SidebarItemViewModel? item)
    {
        if (item is null || !item.IsAvailable)
            return;

        _navigationService.NavigateToModule(item.Name);
        SyncSelectedModule();
    }

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
    private void DiscardAll()
    {
        _pendingChangesService.DiscardAll();
        IsReviewPanelOpen = false;
    }

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [RelayCommand]
    private async Task ApplyAllAsync()
    {
        if (!HasPendingChanges || IsApplying)
            return;

        IsApplying = true;
        StatusMessage = string.Empty;

        try
        {
            var result = await _pendingChangesService.ApplyAllAsync(
                ApplyChangeToModule,
                RevertChangeOnModule).ConfigureAwait(true);

            if (result.IsSuccess)
            {
                await _changeHistoryService.RecordChangesAsync(result).ConfigureAwait(true);
                IsReviewPanelOpen = false;
                StatusMessage = "Changes applied successfully";
            }
            else
            {
                StatusMessage = FormatApplyError(result);
            }
        }
        finally
        {
            IsApplying = false;
            PendingCount = _pendingChangesService.PendingCount;
        }
    }

    private async Task<OperationResult<bool>> ApplyChangeToModule(ChangeDescriptor change)
    {
        var module = _navigationService.Modules
            .FirstOrDefault(m => m.Module.Info.Name == change.ModuleId)?.Module;

        if (module is null)
        {
            return OperationResult<bool>.Failure(
                $"Module '{change.ModuleId}' not found",
                ErrorCategory.NotFound);
        }

        return await module.ApplyChangeAsync(change).ConfigureAwait(false);
    }

    private async Task<OperationResult<bool>> RevertChangeOnModule(ChangeDescriptor change)
    {
        var module = _navigationService.Modules
            .FirstOrDefault(m => m.Module.Info.Name == change.ModuleId)?.Module;

        if (module is null)
        {
            return OperationResult<bool>.Failure(
                $"Module '{change.ModuleId}' not found for revert",
                ErrorCategory.NotFound);
        }

        return await module.RevertChangeAsync(change).ConfigureAwait(false);
    }

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

            ContentTitle = current.Module.Info.Name;
            ContentDescription = current.Module.Info.Description;
        }
    }
}
