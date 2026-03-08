using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.ViewModels;

public partial class ShellSettingViewModel : ViewModelBase
{
    private readonly IPendingChangesService _pendingChangesService;
    private readonly ExplorerPreference? _preference;
    private readonly string _originalValue;
    private bool _suppressStaging;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _systemPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingChange))]
    [NotifyPropertyChangedFor(nameof(IsPendingEnable))]
    [NotifyPropertyChangedFor(nameof(IsPendingDisable))]
    private bool _isEnabled;

    public bool HasPendingChange => IsEnabled.ToString() != _originalValue;
    public bool IsPendingEnable => HasPendingChange && IsEnabled;
    public bool IsPendingDisable => HasPendingChange && !IsEnabled;

    public ShellSettingViewModel(
        ExplorerPreference preference,
        IPendingChangesService pendingChangesService)
    {
        _pendingChangesService = pendingChangesService;
        _preference = preference;
        _originalValue = preference.IsEnabled.ToString();

        Label = preference.DisplayName;
        Description = preference.Description;
        SystemPath = $@"{preference.RegistryKeyPath}\{preference.RegistryValueName}";

        _suppressStaging = true;
        IsEnabled = preference.IsEnabled;
        _suppressStaging = false;
    }

    // Constructor for non-ExplorerPreference settings (taskbar, etc.)
    public ShellSettingViewModel(
        string label,
        string description,
        string systemPath,
        bool isEnabled,
        IPendingChangesService pendingChangesService,
        Func<bool, ChangeDescriptor> changeFactory)
    {
        _pendingChangesService = pendingChangesService;
        _preference = null;
        _changeFactory = changeFactory;
        _originalValue = isEnabled.ToString();

        Label = label;
        Description = description;
        SystemPath = systemPath;

        _suppressStaging = true;
        IsEnabled = isEnabled;
        _suppressStaging = false;
    }

    private readonly Func<bool, ChangeDescriptor>? _changeFactory;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressStaging)
            return;

        ChangeDescriptor change;
        if (_preference is not null)
        {
            change = ExplorerChangeFactory.CreateToggle(_preference, value);
        }
        else if (_changeFactory is not null)
        {
            change = _changeFactory(value);
        }
        else
        {
            return;
        }

        _pendingChangesService.Stage(change);
    }
}
