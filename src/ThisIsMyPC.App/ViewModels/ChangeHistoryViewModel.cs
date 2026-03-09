using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.App.Helpers;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.ViewModels;

public partial class ChangeHistoryViewModel : ViewModelBase
{
    private const int DefaultGroupLimit = 50;

    private readonly IChangeHistoryService _historyService;
    private readonly Func<ChangeDescriptor, Task<OperationResult<bool>>> _revertFunc;
    private readonly Func<ChangeDescriptor, Task<OperationResult<bool>>> _applyFunc;

    public ObservableCollection<ChangeHistoryGroupViewModel> HistoryGroups { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EntryCountText))]
    private int _totalGroupCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EntryCountText))]
    private int _displayedGroupCount;

    public string EntryCountText
    {
        get
        {
            if (TotalGroupCount == 0) return "0 entries";
            if (DisplayedGroupCount < TotalGroupCount)
                return $"Showing {DisplayedGroupCount} of {TotalGroupCount}";
            return TotalGroupCount == 1 ? "1 entry" : $"{TotalGroupCount} entries";
        }
    }

    [ObservableProperty]
    private string? _errorMessage;

    public ChangeHistoryViewModel(
        IChangeHistoryService historyService,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc)
    {
        _historyService = historyService;
        _revertFunc = revertFunc;
        _applyFunc = applyFunc;
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        ErrorMessage = null;

        var entries = await _historyService.GetRecentGroupedAsync(DefaultGroupLimit).ConfigureAwait(true);
        var totalGroups = await _historyService.GetGroupCountAsync().ConfigureAwait(true);

        HistoryGroups.Clear();

        var today = DateTimeOffset.Now.Date;
        var yesterday = today.AddDays(-1);

        // Group entries by GroupId to form batches, then by date
        var batches = entries
            .GroupBy(e => e.GroupId ?? e.Id.ToString(CultureInfo.InvariantCulture))
            .Select(g => BuildBatch(g.ToList()))
            .ToList();

        TotalGroupCount = totalGroups;
        DisplayedGroupCount = batches.Count;

        var dateGroups = batches.GroupBy(b =>
        {
            var date = b.AppliedAt.LocalDateTime.Date;
            if (date == today) return "Today";
            if (date == yesterday) return "Yesterday";
            return date.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture);
        });

        foreach (var dateGroup in dateGroups)
        {
            var groupVm = new ChangeHistoryGroupViewModel { DateHeader = dateGroup.Key };

            foreach (var batch in dateGroup)
                groupVm.Batches.Add(batch);

            HistoryGroups.Add(groupVm);
        }
    }

    private static HistoryBatchViewModel BuildBatch(List<ChangeHistoryEntry> entries)
    {
        var primary = entries[0];

        // Derive a group display name from unique display names
        var uniqueNames = entries.Select(e => e.DisplayName).Distinct().ToList();
        var displayName = uniqueNames.Count == 1
            ? uniqueNames[0]
            : string.Join(", ", uniqueNames);

        var details = entries.Select(e => new ChangeHistoryEntryViewModel
        {
            Id = e.Id,
            DisplayName = e.DisplayName,
            ModuleId = e.ModuleId,
            SystemLocation = e.SystemLocation,
            BeforeDisplay = e.BeforeDisplay ?? string.Empty,
            AfterDisplay = e.AfterDisplay ?? string.Empty,
            Category = e.Category,
            AppliedAt = e.AppliedAt,
            IsReverted = e.RevertedAt.HasValue,
        }).ToList();

        return new HistoryBatchViewModel
        {
            DisplayName = displayName,
            BeforeDisplay = primary.BeforeDisplay ?? string.Empty,
            AfterDisplay = primary.AfterDisplay ?? string.Empty,
            Category = primary.Category,
            AppliedAt = primary.AppliedAt,
            IsReverted = entries.All(e => e.RevertedAt.HasValue),
            GroupId = primary.GroupId ?? primary.Id.ToString(CultureInfo.InvariantCulture),
            Details = details,
        };
    }

    [RelayCommand]
    private async Task RestoreAsync(ChangeHistoryEntryViewModel entry)
    {
        ErrorMessage = null;

        var result = await _historyService.RevertChangeAsync(entry.Id, _revertFunc)
            .ConfigureAwait(true);

        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorCategory.HasValue
                ? ErrorCategoryExtensions.ToGuidance(result.ErrorCategory.Value)
                : result.ErrorMessage ?? "Restore failed.";
            return;
        }

        await LoadHistoryAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RedoAsync(ChangeHistoryEntryViewModel entry)
    {
        ErrorMessage = null;

        var result = await _historyService.RedoChangeAsync(entry.Id, _applyFunc)
            .ConfigureAwait(true);

        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorCategory.HasValue
                ? ErrorCategoryExtensions.ToGuidance(result.ErrorCategory.Value)
                : result.ErrorMessage ?? "Redo failed.";
            return;
        }

        await LoadHistoryAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        await _historyService.ClearHistoryAsync().ConfigureAwait(true);
        await LoadHistoryAsync().ConfigureAwait(true);
    }
}
