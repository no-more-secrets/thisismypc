using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>One selectable value of a multi-choice setting row.</summary>
public sealed record ShellChoiceOption(int Value, string DisplayName);

/// <summary>
/// A setting with 3+ states rendered as a combo row (taskbar search mode,
/// button combining). Staging follows the proven ShellSettingViewModel
/// lifecycle: 250 ms debounce, live baseline re-read at stage time,
/// unstage-then-stage, stage only when the selection differs from registry
/// truth, revert-on-discard / baseline-adopt-on-apply.
/// </summary>
public sealed partial class ShellChoiceSettingViewModel : ViewModelBase, IDisposable
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.App.ViewModels.ShellChoiceSettingViewModel");

    private readonly IPendingChangesService _pendingChangesService;
    private readonly Func<int, ChangeDescriptor> _changeFactory;
    private readonly Func<int> _readRegistryValue;
    private int _registryValue;
    private bool _suppressStaging;
    private bool _isStagingChange;
    private bool _disposed;
    private string? _stagedGroupId;
    private CancellationTokenSource? _debounceCts;

    public string Label { get; }
    public string Description { get; }
    public string SystemPath { get; }
    public IReadOnlyList<ShellChoiceOption> Options { get; }

    [ObservableProperty]
    private ShellChoiceOption? _selectedOption;

    [ObservableProperty]
    private bool _hasPendingChange;

    /// <summary>Row templates bind their root visibility here; the owning view's search sets it.</summary>
    [ObservableProperty]
    private bool _isSearchVisible = true;

    public void ApplySearch(string query) =>
        IsSearchVisible = query.Length == 0
            || Label.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || SystemPath.Contains(query, StringComparison.OrdinalIgnoreCase);

    public ShellChoiceSettingViewModel(
        string label,
        string description,
        string systemPath,
        IReadOnlyList<ShellChoiceOption> options,
        int currentValue,
        IPendingChangesService pendingChangesService,
        Func<int, ChangeDescriptor> changeFactory,
        Func<int> readRegistryValue,
        string? rehydrateSettingId = null)
    {
        _pendingChangesService = pendingChangesService;
        _changeFactory = changeFactory;
        _readRegistryValue = readRegistryValue;
        _registryValue = currentValue;

        Label = label;
        Description = description;
        SystemPath = systemPath;
        Options = options;

        _suppressStaging = true;
        SelectedOption = options.FirstOrDefault(o => o.Value == currentValue) ?? options[0];
        _suppressStaging = false;

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;

        if (rehydrateSettingId is not null)
            RehydrateStagedGroup(rehydrateSettingId);
    }

    private void RehydrateStagedGroup(string settingId)
    {
        var existing = _pendingChangesService.PendingGroups.FirstOrDefault(group =>
            group.Changes.Count == 1 && group.Changes[0].SettingId == settingId);
        if (existing is null)
            return;

        var change = existing.Changes[0];
        if (!int.TryParse(change.AfterValue, System.Globalization.CultureInfo.InvariantCulture, out var pendingValue))
            return;
        if (pendingValue == _registryValue)
        {
            _pendingChangesService.Unstage(existing.GroupId);
            return;
        }

        var pendingOption = Options.FirstOrDefault(option => option.Value == pendingValue);
        if (pendingOption is null)
            return;

        _stagedGroupId = existing.GroupId;
        _suppressStaging = true;
        SelectedOption = pendingOption;
        _suppressStaging = false;
        UpdatePendingState();
    }

    partial void OnSelectedOptionChanged(ShellChoiceOption? value)
    {
        if (_suppressStaging || value is null)
            return;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        _ = DebounceSelectionAsync(value.Value, _debounceCts.Token);
    }

    private async Task DebounceSelectionAsync(int desiredValue, CancellationToken token)
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

            _registryValue = _readRegistryValue();

            var change = _changeFactory(desiredValue);
            var group = new ChangeGroup
            {
                GroupId = Guid.NewGuid().ToString("N"),
                DisplayName = change.DisplayName,
                Description = change.DisplayName,
                Changes = [change],
            };

            _isStagingChange = true;
            try
            {
                if (_stagedGroupId is not null)
                {
                    _pendingChangesService.Unstage(_stagedGroupId);
                    _stagedGroupId = null;
                }

                if (desiredValue != _registryValue)
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
            Log.Error(ex, "Choice staging failed for {Setting}", Label);
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
                // Applied; keep selection, adopt as new baseline.
                _registryValue = SelectedOption?.Value ?? _registryValue;
            }
            else
            {
                // Discarded; reset selection to registry state.
                _suppressStaging = true;
                SelectedOption = Options.FirstOrDefault(o => o.Value == _registryValue) ?? Options[0];
                _suppressStaging = false;
            }

            UpdatePendingState();
        }
    }

    private void UpdatePendingState() =>
        HasPendingChange = SelectedOption?.Value != _registryValue;

    public void Dispose()
    {
        _disposed = true;
        _pendingChangesService.PropertyChanged -= OnPendingChangesPropertyChanged;
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }
}
