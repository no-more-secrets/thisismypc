using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Existing Settings controls show restoration authorization separately from SCM status.
/// Failed operations remain visible inline; missing service evidence means unavailable.
/// </summary>
public partial class OwnerModeSectionViewModel : ViewModelBase
{
    private readonly IOwnerModeServiceControl _ownerMode;

    [ObservableProperty]
    private string _stateText = "";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isRestorationEnabled;

    [ObservableProperty]
    private string _serviceText = "";

    [ObservableProperty]
    private string _statusDetail = "Trusted restoration is unavailable in this build.";

    private bool _consentGranted;
    private int _refreshGeneration;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorText = "";

    public string Description =>
        "Restores supported settings to your saved choices. Protected changes made outside " +
        "ThisIsMyPC will also be restored. Pause before changing them in another app.";

    public OwnerModeSectionViewModel(IOwnerModeServiceControl ownerMode)
    {
        ArgumentNullException.ThrowIfNull(ownerMode);
        _ownerMode = ownerMode;
        RefreshState();
        Initialization = RefreshRestorationAsync();
    }

    public Task Initialization { get; }

    [RelayCommand]
    private async Task RefreshRestorationAsync()
    {
        RefreshState();
        var generation = ++_refreshGeneration;
        try
        {
            var status = await _ownerMode.GetRestorationStatusAsync();
            if (generation != _refreshGeneration) return;
            _consentGranted = status.ConsentGranted;
            IsRestorationEnabled = status is { State: RestorationServiceState.Enabled, ConsentGranted: true };
            StateText = status.State switch
            {
                RestorationServiceState.Enabled when status.ConsentGranted => "Enabled",
                RestorationServiceState.Paused when !status.ConsentGranted => "Paused",
                RestorationServiceState.Conflict => "Conflict",
                _ => "Unavailable",
            };
            StatusDetail = status.Detail;
        }
        catch (Exception ex)
        {
            if (generation != _refreshGeneration) return;
            _consentGranted = false;
            IsRestorationEnabled = false;
            StateText = "Unavailable";
            StatusDetail = "Status could not be read: " + ex.Message;
        }
        EnableCommand.NotifyCanExecuteChanged();
        DisableCommand.NotifyCanExecuteChanged();
    }

    private void RefreshState()
    {
        var state = _ownerMode.GetState();
        IsRunning = state == OwnerModeState.Running;
        ServiceText = state switch
        {
            OwnerModeState.Running => "Service running",
            OwnerModeState.Stopped => "Installed, not running",
            OwnerModeState.Disabled => "Installed, disabled",
            OwnerModeState.Unknown => "State unavailable (service manager query failed)",
            _ => "Not installed",
        };
        EnableCommand.NotifyCanExecuteChanged();
        DisableCommand.NotifyCanExecuteChanged();
    }

    private bool CanEnable() => !IsBusy && StateText != "Enabled";
    private bool CanDisable() => !IsBusy && (IsRunning || _consentGranted);

    [RelayCommand(CanExecute = nameof(CanEnable))]
    private async Task EnableAsync()
    {
        IsBusy = true;
        ++_refreshGeneration;
        EnableCommand.NotifyCanExecuteChanged();
        DisableCommand.NotifyCanExecuteChanged();
        ErrorText = "";
        try
        {
            var result = await _ownerMode.EnableAsync();
            if (!result.IsSuccess)
                ErrorText = result.ErrorMessage ?? "Enabling the service failed.";
        }
        catch (Exception ex) { ErrorText = "Enabling restoration failed: " + ex.Message; }
        finally
        {
            IsBusy = false;
            RefreshState();
            await RefreshRestorationAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisable))]
    private async Task DisableAsync()
    {
        IsBusy = true;
        ++_refreshGeneration;
        EnableCommand.NotifyCanExecuteChanged();
        DisableCommand.NotifyCanExecuteChanged();
        ErrorText = "";
        try
        {
            var result = await _ownerMode.DisableAsync();
            if (!result.IsSuccess)
                ErrorText = result.ErrorMessage ?? "Disabling the service failed.";
        }
        catch (Exception ex) { ErrorText = "Pausing restoration failed: " + ex.Message; }
        finally
        {
            IsBusy = false;
            RefreshState();
            await RefreshRestorationAsync();
        }
    }
}
