using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.App.ViewModels;

public partial class HistoryBatchViewModel : ViewModelBase
{
    public required string DisplayName { get; init; }
    public required string BeforeDisplay { get; init; }
    public required string AfterDisplay { get; init; }
    public required ChangeCategory Category { get; init; }
    public required DateTimeOffset AppliedAt { get; init; }
    public required bool IsReverted { get; init; }
    public required string GroupId { get; init; }
    public required IReadOnlyList<ChangeHistoryEntryViewModel> Details { get; init; }

    [ObservableProperty]
    private bool _isExpanded;

    public ChangeHistoryEntryViewModel PrimaryEntry => Details[0];
    public string AppliedAtDisplay => AppliedAt.LocalDateTime.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    public int DetailCount => Details.Count;
    public bool HasMultipleDetails => Details.Count > 1;
    public string DetailCountText => DetailCount == 1
        ? "(1 registry operation)"
        : $"({DetailCount} registry operations)";

    public bool IsEnableOrCreate => Category is ChangeCategory.Enable or ChangeCategory.Create;
    public bool IsDisableOrDelete => Category is ChangeCategory.Disable or ChangeCategory.Delete;
    public bool IsModifyCategory => Category is ChangeCategory.Modify;
}
