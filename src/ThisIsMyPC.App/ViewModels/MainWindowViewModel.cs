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
    private readonly NavigationService _navigationService;
    private readonly IPendingChangesService _pendingChangesService;
    private readonly IChangeHistoryService _changeHistoryService;
    private readonly IRegistryService _registryService;
    private readonly IExplorerRestartService _explorerRestartService;

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
        ReviewPanelViewModel reviewPanel)
    {
        _navigationService = navigationService;
        _pendingChangesService = pendingChangesService;
        _changeHistoryService = changeHistoryService;
        _registryService = registryService;
        _explorerRestartService = explorerRestartService;
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
                        ContentTitle = current.Module.Info.Name;
                        ContentDescription = current.Module.Info.Description;
                        CurrentContent = new ShellViewModel(scanData, _pendingChangesService, _registryService);
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
            else if (current?.Module is Modules.Annoyances.AnnoyancesModule)
            {
                var scanResult = await current.Module.ScanSystemStateAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (scanResult.IsSuccess && scanResult.Value is Modules.Annoyances.Models.AnnoyancesScanData annoyancesData)
                    {
                        ContentTitle = current.Module.Info.Name;
                        ContentDescription = current.Module.Info.Description;
                        CurrentContent = new AnnoyancesViewModel(annoyancesData, _pendingChangesService, _registryService);
                    }
                    else
                    {
                        CurrentContent = null;
                        SetStatus(scanResult.ErrorMessage ?? "Failed to scan annoyance settings", StatusSeverity.Error);
                    }
                });
            }
            else if (current?.Module is EnvironmentModule)
            {
                var scanResult = await current.Module.ScanSystemStateAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
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
            await Dispatcher.UIThread.InvokeAsync(() =>
                SetStatus($"Failed to load module: {ex.Message}", StatusSeverity.Error));
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
        IsReviewPanelOpen = false;
    }

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private IBrush _statusBrush = Brushes.Transparent;

    private void SetStatus(string message, StatusSeverity severity)
    {
        StatusMessage = message;
        StatusBrush = GetBrush(severity switch
        {
            StatusSeverity.Success => "SuccessBrush",
            StatusSeverity.Error => "DangerBrush",
            StatusSeverity.Warning => "WarningBrush",
            _ => "WarningBrush",
        });
    }

    private static IBrush GetBrush(string key)
    {
        if (Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var resource) == true
            && resource is IBrush brush)
        {
            return brush;
        }
        return Brushes.White;
    }

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
                    SetStatus("Changes applied — reboot required", StatusSeverity.Warning);
                }
                else if (result.RequiredRestarts.Contains(RestartRequirement.SignOut))
                {
                    RestartNotificationMessage = "Sign out and back in for some changes to take effect.";
                    IsRestartActionAvailable = false;
                    IsRestartNotificationVisible = true;
                    SetStatus("Changes applied — sign-out required", StatusSeverity.Warning);
                }
                else if (result.RequiredRestarts.Contains(RestartRequirement.ExplorerRestart))
                {
                    RestartNotificationMessage = "Explorer restart required for changes to take effect. Open file explorer windows may close.";
                    IsRestartActionAvailable = true;
                    IsRestartNotificationVisible = true;
                    SetStatus("Changes applied — Explorer restart needed", StatusSeverity.Warning);
                }
                else if (result.RequiredRestarts.Contains(RestartRequirement.ExplorerRefresh))
                {
                    // Fire-and-forget: trigger SHChangeNotify to refresh Explorer views
                    _ = _explorerRestartService.RefreshExplorerViewsAsync();

                    RestartNotificationMessage = "Explorer preferences updated — open windows may need F5 to refresh";
                    IsRestartActionAvailable = false;
                    IsRestartNotificationVisible = true;
                    SetStatus("Changes applied — Explorer refresh may be needed", StatusSeverity.Success);
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
        }
        finally
        {
            IsApplying = false;
            PendingCount = _pendingChangesService.PendingCount;
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

    private async Task<OperationResult<bool>> ApplyChangeToModule(ChangeDescriptor change)
    {
        var module = ResolveModule(change.ModuleId);

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
        var module = ResolveModule(change.ModuleId);

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
        }
    }
}
