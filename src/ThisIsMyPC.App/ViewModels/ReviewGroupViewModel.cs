using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.App.ViewModels;

public partial class ReviewGroupViewModel : ViewModelBase
{
    public required string DisplayName { get; init; }
    public required string BeforeDisplay { get; init; }
    public required string AfterDisplay { get; init; }
    public required ChangeCategory Category { get; init; }
    public required string GroupId { get; init; }
    public required IReadOnlyList<ReviewItemViewModel> Details { get; init; }

    [ObservableProperty]
    private bool _isExpanded;

    public int DetailCount => Details.Count;
    public bool HasMultipleDetails => Details.Count > 1;
    public string DetailCountText => DetailCount == 1
        ? "(1 registry operation)"
        : $"({DetailCount} registry operations)";

    public bool IsEnableOrCreate => Category is ChangeCategory.Enable or ChangeCategory.Create;
    public bool IsDisableOrDelete => Category is ChangeCategory.Disable or ChangeCategory.Delete;
    public bool IsModifyCategory => Category is ChangeCategory.Modify;
}
