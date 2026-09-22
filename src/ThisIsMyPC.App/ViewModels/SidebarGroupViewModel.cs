using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// One sidebar section. Expansion is not a user toggle: MainWindowViewModel
/// opens the group whose module is on screen and folds the rest (accordion);
/// clicking a folded header opens that group's first module.
/// </summary>
public partial class SidebarGroupViewModel : ViewModelBase
{
    public required string GroupName { get; init; }
    public ObservableCollection<SidebarItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderDescription))]
    private bool _isExpanded = true;

    /// <summary>Accessible name and tooltip of the header: what a click does, or the group name when it does nothing.</summary>
    public string HeaderDescription => IsExpanded ? GroupName : $"Open {GroupName}";
}
