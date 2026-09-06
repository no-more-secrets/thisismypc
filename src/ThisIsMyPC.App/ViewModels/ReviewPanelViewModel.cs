using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using ThisIsMyPC.Core.Changes;
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

    /// <summary>At least one staged group was left in an unknown state by an earlier apply.</summary>
    public bool HasUnresolvedGroups => ReviewGroups.Any(g => g.NeedsReview);

    /// <summary>
    /// The recovery instructions shown above the list while
    /// <see cref="HasUnresolvedGroups"/>. Discarding clears the queue only; it
    /// does not undo anything on the PC, and the text says so.
    /// </summary>
    public string UnresolvedNotice
    {
        get
        {
            var count = ReviewGroups.Count(g => g.NeedsReview);
            if (count == 0)
                return string.Empty;

            var what = count == 1
                ? "One change below did not finish, so its current setting is unknown."
                : $"{count} changes below did not finish, so their current settings are unknown.";
            return what
                + " Nothing can be applied until it is cleared. Click Discard All: the page reloads and shows what Windows has now."
                + " Then set the change again if you still want it. Discard All does not undo anything that already happened.";
        }
    }

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
        if (e.PropertyName is nameof(IPendingChangesService.PendingGroups)
            or nameof(IPendingChangesService.ReconciliationRequired))
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

        // Records are keyed by group instance in the queue; match the same way so
        // a fresh group staged under an old id never inherits the old mark.
        var unresolved = _pendingChangesService.ReconciliationRequired
            .ToDictionary(r => r.Group, r => r, ReferenceEqualityComparer.Instance);

        foreach (var group in _pendingChangesService.PendingGroups)
        {
            if (group.Changes.Count == 0)
                continue;

            var record = unresolved.GetValueOrDefault(group);

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
                NeedsReview = record is not null,
                ReviewNote = record is null ? string.Empty : DescribeUnresolved(record),
            });
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HeaderCountText));
        OnPropertyChanged(nameof(HasUnresolvedGroups));
        OnPropertyChanged(nameof(UnresolvedNotice));
    }

    /// <summary>One line under the group: what stopped it and which values are unknown.</summary>
    public static string DescribeUnresolved(GroupReconciliation record)
    {
        var names = MainWindowViewModel.NameList(record.Uncertain);
        var unknown = record.Uncertain.Count == 1
            ? $"Current value of {names} is unknown."
            : $"Current values of {names} are unknown.";

        switch (record.Kind)
        {
            case MutationFailureKind.Cancelled:
                return $"Cancelled, but {names} could not be put back. {unknown}";

            case MutationFailureKind.ChangeFailed:
            case MutationFailureKind.ChangeThrew:
            default:
            {
                var at = record.Failed?.DisplayName ?? record.Uncertain[0].DisplayName;
                var reason = record.ErrorMessage is { Length: > 0 } text ? $": {text.TrimEnd('.')}" : "";
                var stuck = record.RollbackFailures.Count == 0
                    ? ""
                    : $" {MainWindowViewModel.NameList(record.RollbackFailures.Select(f => f.Change).ToList())} could not be put back.";
                return $"Did not finish at \"{at}\"{reason}.{stuck} {unknown}";
            }
        }
    }

    public void Dispose()
    {
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
        if (_pendingActionsService is not null)
            _pendingActionsService.PropertyChanged -= OnPendingActionsPropertyChanged;
    }
}
