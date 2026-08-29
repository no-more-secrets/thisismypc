using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Interactive wrapper around a module-provided SettingCardSource (Epic 10). Toggle
/// mechanics follow the proven ShellSettingViewModel pattern: 250 ms debounce, live
/// baseline re-read at stage time, unstage-then-stage, stage only when the desired
/// state differs from registry truth, revert-on-discard / baseline-adopt-on-apply.
/// Display-mode flags (description/registry visibility) are set by the owning tab VM.
/// </summary>
public sealed partial class SettingCardViewModel : ViewModelBase, IDisposable
{
    private readonly IPendingChangesService _pendingChangesService;
    private readonly SettingCardSource _source;
    private bool _registryIsEnabled;
    private bool _suppressStaging;
    private bool _isStagingChange;
    private bool _disposed;
    private string? _stagedGroupId;
    private CancellationTokenSource? _debounceCts;

    public SettingCardModel Model { get; }

    public string DisplayName => Model.DisplayName;
    public string Description => Model.Description;
    public bool IsToggle => Model.ControlType == SettingControlType.Toggle;
    public string SystemPath => Model.RegistryPath is null
        ? string.Empty
        : Model.ValueName is null ? Model.RegistryPath : $@"{Model.RegistryPath}\{Model.ValueName}";

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _hasPendingChange;

    [ObservableProperty]
    private bool _isPendingEnable;

    [ObservableProperty]
    private bool _isPendingDisable;

    /// <summary>Collapsed in Compact mode (10-2); expanded by default.</summary>
    [ObservableProperty]
    private bool _isDescriptionVisible = true;

    /// <summary>Registry Data mode (10-2); hidden by default.</summary>
    [ObservableProperty]
    private bool _isRegistryDataVisible;

    public SettingCardViewModel(SettingCardSource source, IPendingChangesService pendingChangesService)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pendingChangesService);
        _source = source;
        _pendingChangesService = pendingChangesService;
        Model = source.Model;

        _registryIsEnabled = Model.CurrentValue == "1";
        _suppressStaging = true;
        IsEnabled = _registryIsEnabled;
        _suppressStaging = false;

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressStaging)
            return;

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
            if (_disposed)
                return;

            _registryIsEnabled = _source.ReadCurrentState();

            var group = _source.CreateToggleGroup(desiredState);

            _isStagingChange = true;
            try
            {
                if (_stagedGroupId is not null)
                {
                    _pendingChangesService.Unstage(_stagedGroupId);
                    _stagedGroupId = null;
                }

                if (desiredState != _registryIsEnabled)
                {
                    _pendingChangesService.Stage(group);
                    _stagedGroupId = group.GroupId;
                }
            }
            finally
            {
                _isStagingChange = false;
            }

            UpdatePendingState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toggle staging failed for {DisplayName}: {ex.Message}");
        }
    }

    private void OnPendingChangesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isStagingChange)
            return;
        if (e.PropertyName is not nameof(IPendingChangesService.PendingGroups))
            return;

        if (Dispatcher.UIThread.CheckAccess())
            HandlePendingGroupsChanged();
        else
            Dispatcher.UIThread.Post(HandlePendingGroupsChanged);
    }

    private void HandlePendingGroupsChanged()
    {
        if (_stagedGroupId is not null &&
            !_pendingChangesService.PendingGroups.Any(g => g.GroupId == _stagedGroupId))
        {
            _stagedGroupId = null;

            if (_pendingChangesService.IsApplying)
            {
                // Applied — keep toggle position, adopt as new baseline.
                _registryIsEnabled = IsEnabled;
            }
            else
            {
                // Discarded — reset toggle to registry state.
                _suppressStaging = true;
                IsEnabled = _registryIsEnabled;
                _suppressStaging = false;
            }

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
        _disposed = true;
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }
}
