using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.ViewModels;

public sealed partial class ShellSettingViewModel : ViewModelBase, IDisposable
{
    private readonly IPendingChangesService _pendingChangesService;
    private readonly ExplorerPreference? _preference;
    private readonly Func<bool, ChangeDescriptor>? _changeFactory;
    private readonly Func<bool>? _readRegistryState;
    private bool _registryIsEnabled;
    private bool _suppressStaging;
    private bool _isStagingChange;
    private string? _stagedGroupId;
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

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
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

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressStaging)
            return;

        // Cancel any in-flight debounce so only the final toggle state is processed
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
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

        try
        {
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

            _isStagingChange = true;
            try
            {
                // Unstage any existing pending change
                if (_stagedGroupId is not null)
                {
                    _pendingChangesService.Unstage(_stagedGroupId);
                    _stagedGroupId = null;
                }

                // Only stage if the desired state differs from the real registry value
                if (desiredState != _registryIsEnabled)
                {
                    var group = new ChangeGroup
                    {
                        GroupId = Guid.NewGuid().ToString("N"),
                        DisplayName = change.DisplayName,
                        Description = change.DisplayName,
                        Changes = [change]
                    };
                    _pendingChangesService.Stage(group);
                    _stagedGroupId = group.GroupId;
                }
            }
            finally
            {
                _isStagingChange = false;
            }

            // Update pending state properties for UI binding
            UpdatePendingState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toggle staging failed for {Label}: {ex.Message}");
        }
    }

    private void OnPendingChangesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isStagingChange)
            return;
        if (e.PropertyName is not nameof(IPendingChangesService.PendingGroups))
            return;

        // Our staged change was removed externally (DiscardAll or Unstage) — reset toggle
        if (_stagedGroupId is not null &&
            !_pendingChangesService.PendingGroups.Any(g => g.GroupId == _stagedGroupId))
        {
            _stagedGroupId = null;
            _suppressStaging = true;
            IsEnabled = _registryIsEnabled;
            _suppressStaging = false;
            UpdatePendingState();
        }
    }

    private void UpdatePendingState()
    {
        HasPendingChange = IsEnabled != _registryIsEnabled;
        IsPendingEnable = HasPendingChange && IsEnabled;
        IsPendingDisable = HasPendingChange && !IsEnabled;
    }

    public void Dispose()
    {
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }
}
