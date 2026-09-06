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

    /// <summary>
    /// True when an earlier apply stopped inside this group and left at least
    /// one of its values unknown. The group is still staged, but the queue will
    /// not apply anything until it is discarded.
    /// </summary>
    public bool NeedsReview { get; init; }

    /// <summary>What went wrong, in the person's words; empty unless <see cref="NeedsReview"/>.</summary>
    public string ReviewNote { get; init; } = string.Empty;

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
