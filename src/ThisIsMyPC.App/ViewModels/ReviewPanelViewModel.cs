using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.ViewModels;

public partial class ReviewPanelViewModel : ViewModelBase
{
    private readonly IPendingChangesService _pendingChangesService;

    public ObservableCollection<ReviewItemViewModel> ReviewItems { get; } = [];

    public ReviewPanelViewModel(IPendingChangesService pendingChangesService)
    {
        _pendingChangesService = pendingChangesService;
        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
        RefreshItems();
    }

    private void OnPendingChangesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IPendingChangesService.PendingGroups))
        {
            if (Dispatcher.UIThread.CheckAccess())
                RefreshItems();
            else
                Dispatcher.UIThread.Post(RefreshItems);
        }
    }

    private void RefreshItems()
    {
        ReviewItems.Clear();

        foreach (var group in _pendingChangesService.PendingGroups)
        {
            foreach (var change in group.Changes)
            {
                ReviewItems.Add(new ReviewItemViewModel
                {
                    DisplayName = change.DisplayName,
                    Description = group.Description,
                    SystemLocation = change.SystemLocation,
                    BeforeDisplay = change.BeforeDisplay,
                    AfterDisplay = change.AfterDisplay ?? string.Empty,
                    Category = change.Category,
                    GroupId = group.GroupId,
                    SettingId = change.SettingId,
                });
            }
        }
    }
}
