using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.App.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Settings section for the Owner Mode service lifecycle (28-2). State is
/// re-queried from the SCM on construction and after every action; failures
/// leave the section in the current real state with the error inline.
/// </summary>
public partial class OwnerModeSectionViewModel : ViewModelBase
{
    private readonly OwnerModeService _ownerMode;

    [ObservableProperty]
    private string _stateText = "";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorText = "";

    public string Description =>
        "Detects when Windows reverts your settings after updates. Runs as a background " +
        "service so drift is caught at boot, before you open the app. Optional; everything " +
        "else works without it.";

    public OwnerModeSectionViewModel(OwnerModeService ownerMode)
    {
        ArgumentNullException.ThrowIfNull(ownerMode);
        _ownerMode = ownerMode;
        RefreshState();
    }

    private void RefreshState()
    {
        var state = _ownerMode.GetState();
        IsRunning = state == OwnerModeState.Running;
        StateText = state switch
        {
            OwnerModeState.Running => "Running",
            OwnerModeState.Stopped => "Installed, not running",
            OwnerModeState.Disabled => "Installed, disabled",
            OwnerModeState.Unknown => "State unavailable (service manager query failed)",
            _ => "Not installed",
        };
        EnableCommand.NotifyCanExecuteChanged();
        DisableCommand.NotifyCanExecuteChanged();
    }

    private bool CanEnable() => !IsBusy && !IsRunning;
    private bool CanDisable() => !IsBusy && IsRunning;

    [RelayCommand(CanExecute = nameof(CanEnable))]
    private async Task EnableAsync()
    {
        IsBusy = true;
        ErrorText = "";
        try
        {
            var result = await _ownerMode.EnableAsync();
            if (!result.IsSuccess)
                ErrorText = result.ErrorMessage ?? "Enabling the service failed.";
        }
        finally
        {
            IsBusy = false;
            RefreshState();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisable))]
    private async Task DisableAsync()
    {
        IsBusy = true;
        ErrorText = "";
        try
        {
            var result = await _ownerMode.DisableAsync();
            if (!result.IsSuccess)
                ErrorText = result.ErrorMessage ?? "Disabling the service failed.";
        }
        finally
        {
            IsBusy = false;
            RefreshState();
        }
    }
}
