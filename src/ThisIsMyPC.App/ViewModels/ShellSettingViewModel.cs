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
    private readonly Func<bool, ChangeDescriptor>? _changeFactory;
    private readonly bool _originalIsEnabled;
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

    public bool HasPendingChange => IsEnabled != _originalIsEnabled;
    public bool IsPendingEnable => HasPendingChange && IsEnabled;
    public bool IsPendingDisable => HasPendingChange && !IsEnabled;

    public ShellSettingViewModel(
        ExplorerPreference preference,
        IPendingChangesService pendingChangesService)
    {
        _pendingChangesService = pendingChangesService;
        _preference = preference;
        _originalIsEnabled = preference.IsEnabled;

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
        _originalIsEnabled = isEnabled;

        Label = label;
        Description = description;
        SystemPath = systemPath;

        _suppressStaging = true;
        IsEnabled = isEnabled;
        _suppressStaging = false;
    }

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

        // Remove any existing pending change for the same setting before staging
        var existing = _pendingChangesService.PendingGroups
            .FirstOrDefault(g => g.Changes.Any(c => c.SettingId == change.SettingId));
        if (existing is not null)
            _pendingChangesService.Unstage(existing.GroupId);

        // Only stage if the value differs from the original
        if (HasPendingChange)
            _pendingChangesService.Stage(change);
    }
}
