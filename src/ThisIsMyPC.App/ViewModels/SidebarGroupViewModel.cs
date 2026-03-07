using System.Collections.ObjectModel;

namespace ThisIsMyPC.App.ViewModels;

public class SidebarGroupViewModel
{
    public required string GroupName { get; init; }
    public ObservableCollection<SidebarItemViewModel> Items { get; } = [];
}
