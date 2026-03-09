using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.ViewModels;

public partial class EnvironmentViewModel : ViewModelBase
{
    private readonly IPendingChangesService _pendingChangesService;

    public ObservableCollection<EnvironmentVariableItemViewModel> UserVariables { get; } = [];
    public ObservableCollection<EnvironmentVariableItemViewModel> SystemVariables { get; } = [];

    [ObservableProperty]
    private PathEditorViewModel? _activePathEditor;

    [ObservableProperty]
    private bool _isPathEditorOpen;

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
            var item = new EnvironmentVariableItemViewModel(envVar, pendingChangesService);
            item.RequestRemoval += OnItemRequestRemoval;
            UserVariables.Add(item);
        }

        foreach (var envVar in scanData.SystemVariables)
        {
            var item = new EnvironmentVariableItemViewModel(envVar, pendingChangesService);
            item.RequestRemoval += OnItemRequestRemoval;
            SystemVariables.Add(item);
        }
    }

    [RelayCommand]
    private void AddUserVariable()
    {
        var item = new EnvironmentVariableItemViewModel("User", _pendingChangesService,
            name => !UserVariables.Any(v => !v.IsNew && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        item.RequestRemoval += OnItemRequestRemoval;
        UserVariables.Add(item);
    }

    [RelayCommand]
    private void AddSystemVariable()
    {
        var item = new EnvironmentVariableItemViewModel("System", _pendingChangesService,
            name => !SystemVariables.Any(v => !v.IsNew && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        item.RequestRemoval += OnItemRequestRemoval;
        SystemVariables.Add(item);
    }

    [RelayCommand]
    private void EditPath(EnvironmentVariableItemViewModel item)
    {
        ActivePathEditor = new PathEditorViewModel(item.Value, item.Scope, _pendingChangesService);
        IsPathEditorOpen = true;
    }

    [RelayCommand]
    private void ClosePathEditor()
    {
        IsPathEditorOpen = false;
        ActivePathEditor = null;
    }

    private void OnItemRequestRemoval(EnvironmentVariableItemViewModel item)
    {
        item.RequestRemoval -= OnItemRequestRemoval;
        UserVariables.Remove(item);
        SystemVariables.Remove(item);
    }
}
