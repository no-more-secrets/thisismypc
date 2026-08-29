using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.App.ViewModels;

public partial class ReviewPanelViewModel : ViewModelBase, IDisposable
{
    private readonly IPendingChangesService _pendingChangesService;

    public ObservableCollection<ReviewGroupViewModel> ReviewGroups { get; } = [];

    public SaveSetFormViewModel SaveSetForm { get; }

    public ReviewPanelViewModel(IPendingChangesService pendingChangesService, ICustomSetWriter customSetWriter)
    {
        _pendingChangesService = pendingChangesService;
        SaveSetForm = new SaveSetFormViewModel(metadata =>
            customSetWriter.WriteFromPendingGroups(metadata, _pendingChangesService.PendingGroups));
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
        ReviewGroups.Clear();

        foreach (var group in _pendingChangesService.PendingGroups)
        {
            if (group.Changes.Count == 0)
                continue;

            var details = group.Changes.Select(change => new ReviewItemViewModel
            {
                DisplayName = change.DisplayName,
                Description = group.Description,
                SystemLocation = change.SystemLocation,
                BeforeDisplay = change.BeforeDisplay,
                AfterDisplay = change.AfterDisplay ?? string.Empty,
                Category = change.Category,
                GroupId = group.GroupId,
                SettingId = change.SettingId,
            }).ToList();

            var primary = group.Changes[0];

            ReviewGroups.Add(new ReviewGroupViewModel
            {
                DisplayName = group.DisplayName,
                BeforeDisplay = primary.BeforeDisplay,
                AfterDisplay = primary.AfterDisplay ?? string.Empty,
                Category = primary.Category,
                GroupId = group.GroupId,
                Details = details,
            });
        }
    }

    public void Dispose()
    {
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
    }
}
