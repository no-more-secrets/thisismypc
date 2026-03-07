using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.App.ViewModels;

public partial class ReviewItemViewModel : ViewModelBase
{
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string SystemLocation { get; init; }
    public required string BeforeDisplay { get; init; }
    public required string AfterDisplay { get; init; }
    public required ChangeCategory Category { get; init; }
    public required string GroupId { get; init; }
    public required string SettingId { get; init; }

    [ObservableProperty]
    private bool _isIncluded = true;

    public string TintClass => Category switch
    {
        ChangeCategory.Enable or ChangeCategory.Create => "pending-enable",
        ChangeCategory.Disable or ChangeCategory.Delete => "pending-disable",
        _ => "pending-modify",
    };

    public bool IsEnableOrCreate => Category is ChangeCategory.Enable or ChangeCategory.Create;
    public bool IsDisableOrDelete => Category is ChangeCategory.Disable or ChangeCategory.Delete;
    public bool IsModifyCategory => Category is ChangeCategory.Modify;
}
