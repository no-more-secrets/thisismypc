using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ThisIsMyPC.App.ViewModels;

public partial class SidebarGroupViewModel : ViewModelBase
{
    public required string GroupName { get; init; }
    public ObservableCollection<SidebarItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleDescription))]
    private bool _isExpanded = true;

    public string ToggleDescription => $"{(IsExpanded ? "Collapse" : "Expand")} {GroupName}";

    [RelayCommand]
    private void ToggleExpansion() => IsExpanded = !IsExpanded;
}
