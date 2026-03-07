using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.App.Services;

namespace ThisIsMyPC.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly NavigationService _navigationService;

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

    public MainWindowViewModel(NavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    public async Task InitializeAsync()
    {
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
