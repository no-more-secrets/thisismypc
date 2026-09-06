using System.Globalization;
using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.App.ViewModels;

public partial class ChangeHistoryEntryViewModel : ViewModelBase
{
    public required long Id { get; init; }
    public required string DisplayName { get; init; }
    public required string ModuleId { get; init; }
    public required string SystemLocation { get; init; }
    public required string BeforeDisplay { get; init; }
    public required string AfterDisplay { get; init; }
    public required ChangeCategory Category { get; init; }
    public required DateTimeOffset AppliedAt { get; init; }
    public required bool IsReverted { get; init; }

    public bool CanExecuteHistoryAction { get; init; } = true;
    public string? ActionRestriction { get; init; }
    public bool CanRestore => CanExecuteHistoryAction && !IsReverted;
    public bool CanRedo => CanExecuteHistoryAction && IsReverted;

    public string AppliedAtDisplay => AppliedAt.LocalDateTime.ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    public bool IsEnableOrCreate => Category is ChangeCategory.Enable or ChangeCategory.Create;
    public bool IsDisableOrDelete => Category is ChangeCategory.Disable or ChangeCategory.Delete;
    public bool IsModifyCategory => Category is ChangeCategory.Modify;
}
