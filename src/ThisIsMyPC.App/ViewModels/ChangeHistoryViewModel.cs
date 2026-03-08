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
    private readonly IChangeHistoryService _historyService;
    private readonly Func<ChangeDescriptor, Task<OperationResult<bool>>> _revertFunc;
    private readonly Func<ChangeDescriptor, Task<OperationResult<bool>>> _applyFunc;

    public ObservableCollection<ChangeHistoryGroupViewModel> HistoryGroups { get; } = [];

    [ObservableProperty]
    private int _totalEntryCount;

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

        var entries = await _historyService.GetHistoryAsync().ConfigureAwait(true);
        TotalEntryCount = entries.Count;

        HistoryGroups.Clear();

        var today = DateTimeOffset.Now.Date;
        var yesterday = today.AddDays(-1);

        var grouped = entries.GroupBy(e =>
        {
            var date = e.AppliedAt.LocalDateTime.Date;
            if (date == today) return "Today";
            if (date == yesterday) return "Yesterday";
            return date.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture);
        });

        foreach (var group in grouped)
        {
            var groupVm = new ChangeHistoryGroupViewModel { DateHeader = group.Key };

            foreach (var entry in group)
            {
                groupVm.Entries.Add(new ChangeHistoryEntryViewModel
                {
                    Id = entry.Id,
                    DisplayName = entry.DisplayName,
                    ModuleId = entry.ModuleId,
                    SystemLocation = entry.SystemLocation,
                    BeforeDisplay = entry.BeforeDisplay ?? string.Empty,
                    AfterDisplay = entry.AfterDisplay ?? string.Empty,
                    Category = entry.Category,
                    AppliedAt = entry.AppliedAt,
                    IsReverted = entry.RevertedAt.HasValue,
                });
            }

            HistoryGroups.Add(groupVm);
        }
    }

    [RelayCommand]
    private async Task RevertAsync(ChangeHistoryEntryViewModel entry)
    {
        ErrorMessage = null;

        var result = await _historyService.RevertChangeAsync(entry.Id, _revertFunc)
            .ConfigureAwait(true);

        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorCategory.HasValue
                ? ErrorCategoryExtensions.ToGuidance(result.ErrorCategory.Value)
                : result.ErrorMessage ?? "Revert failed.";
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
}
