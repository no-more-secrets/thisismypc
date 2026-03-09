using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.App.ViewModels;

public partial class EnvironmentVariableItemViewModel : ViewModelBase
{
    private readonly IPendingChangesService _pendingChangesService;
    private readonly string _registryKeyPath;
    private readonly Func<string, bool>? _isNameAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPathVariable))]
    private string _name;

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private string _scope;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isNew;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editValue = string.Empty;

    private readonly string _originalValue;

    public bool IsPathVariable => Name.Equals("Path", StringComparison.OrdinalIgnoreCase);

    public EnvironmentVariableItemViewModel(
        EnvironmentVariable envVar,
        IPendingChangesService pendingChangesService)
    {
        _pendingChangesService = pendingChangesService;
        _name = envVar.Name;
        _value = envVar.Value;
        _originalValue = envVar.Value;
        _scope = envVar.Scope.ToString();
        _registryKeyPath = envVar.Scope == EnvironmentVariableScope.User
            ? EnvironmentVariableReader.UserEnvKeyPath
            : EnvironmentVariableReader.SystemEnvKeyPath;
    }

    public EnvironmentVariableItemViewModel(
        string scope,
        IPendingChangesService pendingChangesService,
        Func<string, bool>? isNameAvailable = null)
    {
        _pendingChangesService = pendingChangesService;
        _isNameAvailable = isNameAvailable;
        _name = string.Empty;
        _value = string.Empty;
        _originalValue = string.Empty;
        _scope = scope;
        _isNew = true;
        _isEditing = true;
        _registryKeyPath = string.Equals(scope, "User", StringComparison.OrdinalIgnoreCase)
            ? EnvironmentVariableReader.UserEnvKeyPath
            : EnvironmentVariableReader.SystemEnvKeyPath;
    }

    [RelayCommand]
    private void BeginEdit()
    {
        EditName = Name;
        EditValue = Value;
        IsEditing = true;
    }

    [RelayCommand]
    private void ConfirmEdit()
    {
        if (IsNew)
        {
            if (string.IsNullOrWhiteSpace(EditName))
                return;

            if (_isNameAvailable is not null && !_isNameAvailable(EditName))
                return;

            Name = EditName;
            Value = EditValue;
            IsNew = false;
            IsEditing = false;

            var change = EnvironmentVariableChangeFactory.CreateAdd(
                Name, Value, Scope, _registryKeyPath);
            _pendingChangesService.Stage(change);
        }
        else
        {
            if (EditValue == _originalValue)
            {
                IsEditing = false;
                return;
            }

            Value = EditValue;
            IsEditing = false;

            var change = EnvironmentVariableChangeFactory.CreateModify(
                Name, _originalValue, Value, Scope, _registryKeyPath);
            _pendingChangesService.Stage(change);
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        if (IsNew)
        {
            // Parent should remove this item
            RequestRemoval?.Invoke(this);
        }
    }

    [RelayCommand]
    private void Delete()
    {
        var change = EnvironmentVariableChangeFactory.CreateDelete(
            Name, _originalValue, Scope, _registryKeyPath);
        _pendingChangesService.Stage(change);
        RequestRemoval?.Invoke(this);
    }

    public event Action<EnvironmentVariableItemViewModel>? RequestRemoval;
}
