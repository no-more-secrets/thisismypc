using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.ViewModels;

public partial class EnvironmentViewModel : ViewModelBase
{
    private readonly IPendingChangesService _pendingChangesService;

    // PATH tab; inline editors for user and system PATH
    public PathEditorViewModel? UserPathEditor { get; }
    public PathEditorViewModel? SystemPathEditor { get; }

    // User tab (excluding PATH)
    public ObservableCollection<EnvironmentVariableItemViewModel> UserVariables { get; } = [];

    // System tab (excluding PATH)
    public ObservableCollection<EnvironmentVariableItemViewModel> SystemVariables { get; } = [];

    [ObservableProperty]
    private string _variableSearchText = string.Empty;

    partial void OnVariableSearchTextChanged(string value)
    {
        foreach (var variable in UserVariables)
            variable.ApplySearch(value);
        foreach (var variable in SystemVariables)
            variable.ApplySearch(value);
    }

    public string UserTabHeader => $"User ({UserVariables.Count})";
    public string SystemTabHeader => $"System ({SystemVariables.Count})";

    [ObservableProperty]
    private string? _userScanError;

    [ObservableProperty]
    private string? _systemScanError;

    public EnvironmentViewModel(
        EnvironmentScanData scanData,
        IPendingChangesService pendingChangesService)
    {
        _pendingChangesService = pendingChangesService;
        _userScanError = scanData.UserScanError;
        _systemScanError = scanData.SystemScanError;

        foreach (var envVar in scanData.UserVariables)
        {
            if (envVar.Name.Equals("Path", StringComparison.OrdinalIgnoreCase))
            {
                UserPathEditor = new PathEditorViewModel(envVar.Value, "User", pendingChangesService);
            }
            else
            {
                var item = new EnvironmentVariableItemViewModel(envVar, pendingChangesService);
                item.RequestRemoval += OnItemRequestRemoval;
                UserVariables.Add(item);
            }
        }

        foreach (var envVar in scanData.SystemVariables)
        {
            if (envVar.Name.Equals("Path", StringComparison.OrdinalIgnoreCase))
            {
                SystemPathEditor = new PathEditorViewModel(envVar.Value, "System", pendingChangesService);
            }
            else
            {
                var item = new EnvironmentVariableItemViewModel(envVar, pendingChangesService);
                item.RequestRemoval += OnItemRequestRemoval;
                SystemVariables.Add(item);
            }
        }
    }

    [RelayCommand]
    private void AddUserVariable()
    {
        var item = new EnvironmentVariableItemViewModel("User", _pendingChangesService,
            name => !UserVariables.Any(v => !v.IsNew && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        item.RequestRemoval += OnItemRequestRemoval;
        UserVariables.Add(item);
        OnPropertyChanged(nameof(UserTabHeader));
    }

    [RelayCommand]
    private void AddSystemVariable()
    {
        var item = new EnvironmentVariableItemViewModel("System", _pendingChangesService,
            name => !SystemVariables.Any(v => !v.IsNew && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        item.RequestRemoval += OnItemRequestRemoval;
        SystemVariables.Add(item);
        OnPropertyChanged(nameof(SystemTabHeader));
    }

    private void OnItemRequestRemoval(EnvironmentVariableItemViewModel item)
    {
        item.RequestRemoval -= OnItemRequestRemoval;
        if (UserVariables.Remove(item))
            OnPropertyChanged(nameof(UserTabHeader));
        else if (SystemVariables.Remove(item))
            OnPropertyChanged(nameof(SystemTabHeader));
    }
}
