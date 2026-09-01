using System.ComponentModel;
using System.Windows.Input;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// The member shape ToggleSettingRowTemplate binds against. Typed so the
/// template uses compiled bindings: reflection bindings are the one thing
/// NativeAOT trimming can silently break. Members a row does not use return
/// constants (false/null) and simply hide that part of the template.
/// </summary>
public interface IToggleSettingRow : INotifyPropertyChanged
{
    string Label { get; }
    string Description { get; }
    bool IsEnabled { get; set; }
    bool IsSearchVisible { get; }
    bool IsPendingEnable { get; }
    bool IsPendingDisable { get; }
    bool IsToggleEnabled { get; }
    bool IsInactive { get; }
    string? InactiveReason { get; }
    string? WarningText { get; }
    string? DisableMethodText { get; }
    bool CanMigrate { get; }
    ICommand? MigrateCommand { get; }
}
