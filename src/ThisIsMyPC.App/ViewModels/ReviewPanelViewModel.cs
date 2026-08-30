using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.App.ViewModels;

public partial class ReviewPanelViewModel : ViewModelBase, IDisposable
{
    private readonly IPendingChangesService _pendingChangesService;
    private readonly IPendingActionsService? _pendingActionsService;

    public ObservableCollection<ReviewGroupViewModel> ReviewGroups { get; } = [];

    public ObservableCollection<ReviewActionViewModel> ReviewActions { get; } = [];

    public bool HasActions => ReviewActions.Count > 0;

    public bool IsEmpty => ReviewGroups.Count == 0 && ReviewActions.Count == 0;

    public string HeaderCountText
    {
        get
        {
            if (ReviewActions.Count == 0)
                return $"{ReviewGroups.Count} change(s)";
            if (ReviewGroups.Count == 0)
                return $"{ReviewActions.Count} action(s)";
            return $"{ReviewGroups.Count} change(s), {ReviewActions.Count} action(s)";
        }
    }

    public SaveSetFormViewModel SaveSetForm { get; }

    public ReviewPanelViewModel(
        IPendingChangesService pendingChangesService,
        ICustomSetWriter customSetWriter,
        IPendingActionsService? pendingActionsService = null)
    {
        _pendingChangesService = pendingChangesService;
        _pendingActionsService = pendingActionsService;
        SaveSetForm = new SaveSetFormViewModel(metadata =>
            customSetWriter.WriteFromPendingGroups(metadata, _pendingChangesService.PendingGroups));
        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
        if (_pendingActionsService is not null)
            _pendingActionsService.PropertyChanged += OnPendingActionsPropertyChanged;
        RefreshItems();
        RefreshActions();
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

    private void OnPendingActionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IPendingActionsService.PendingActions))
        {
            if (Dispatcher.UIThread.CheckAccess())
                RefreshActions();
            else
                Dispatcher.UIThread.Post(RefreshActions);
        }
    }

    private void RefreshActions()
    {
        ReviewActions.Clear();

        if (_pendingActionsService is not null)
        {
            foreach (var action in _pendingActionsService.PendingActions)
            {
                ReviewActions.Add(new ReviewActionViewModel
                {
                    ActionId = action.ActionId,
                    DisplayName = action.DisplayName,
                    Detail = action.Detail,
                    UndoHint = action.UndoHint,
                });
            }
        }

        OnPropertyChanged(nameof(HasActions));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HeaderCountText));
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void UnstageAction(string actionId)
    {
        _pendingActionsService?.Unstage(actionId);
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

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HeaderCountText));
    }

    public void Dispose()
    {
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
        if (_pendingActionsService is not null)
            _pendingActionsService.PropertyChanged -= OnPendingActionsPropertyChanged;
    }
}
