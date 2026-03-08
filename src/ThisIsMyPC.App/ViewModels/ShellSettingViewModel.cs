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
    private readonly Func<bool>? _readRegistryState;
    private bool _registryIsEnabled;
    private bool _suppressStaging;
    private CancellationTokenSource? _debounceCts;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _systemPath = string.Empty;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _hasPendingChange;

    [ObservableProperty]
    private bool _isPendingEnable;

    [ObservableProperty]
    private bool _isPendingDisable;

    public ShellSettingViewModel(
        ExplorerPreference preference,
        IPendingChangesService pendingChangesService,
        Func<bool> readRegistryState)
    {
        _pendingChangesService = pendingChangesService;
        _preference = preference;
        _readRegistryState = readRegistryState;
        _registryIsEnabled = preference.IsEnabled;

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
        Func<bool, ChangeDescriptor> changeFactory,
        Func<bool> readRegistryState)
    {
        _pendingChangesService = pendingChangesService;
        _preference = null;
        _changeFactory = changeFactory;
        _readRegistryState = readRegistryState;
        _registryIsEnabled = isEnabled;

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

        // Cancel any in-flight debounce so only the final toggle state is processed
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        _ = DebounceToggleAsync(value, _debounceCts.Token);
    }

    private async Task DebounceToggleAsync(bool desiredState, CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token).ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        // Refresh baseline from registry (source of truth)
        if (_readRegistryState is not null)
            _registryIsEnabled = _readRegistryState();

        // Build the change descriptor
        ChangeDescriptor? change = null;
        if (_preference is not null)
            change = ExplorerChangeFactory.CreateToggle(_preference, desiredState);
        else if (_changeFactory is not null)
            change = _changeFactory(desiredState);

        if (change is null)
            return;

        // Unstage any existing pending change for the same setting
        var existing = _pendingChangesService.PendingGroups
            .FirstOrDefault(g => g.Changes.Any(c => c.SettingId == change.SettingId));
        if (existing is not null)
            _pendingChangesService.Unstage(existing.GroupId);

        // Only stage if the desired state differs from the real registry value
        if (desiredState != _registryIsEnabled)
            _pendingChangesService.Stage(change);

        // Update pending state properties for UI binding
        UpdatePendingState();
    }

    private void UpdatePendingState()
    {
        HasPendingChange = IsEnabled != _registryIsEnabled;
        IsPendingEnable = HasPendingChange && IsEnabled;
        IsPendingDisable = HasPendingChange && !IsEnabled;
    }
}
