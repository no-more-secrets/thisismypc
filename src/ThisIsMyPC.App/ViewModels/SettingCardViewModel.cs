using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.App.Services;
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
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.App.ViewModels.SettingCardViewModel");

    private readonly IPendingChangesService _pendingChangesService;
    private readonly SettingCardSource _source;
    private readonly ICapabilityDetector? _capabilityDetector;
    private readonly IOwnerModeLifecycle? _ownerMode;
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

    /// <summary>Card templates bind their root visibility here; the owning tab's search sets it.</summary>
    [ObservableProperty]
    private bool _isSearchVisible = true;

    public void ApplySearch(string query) =>
        IsSearchVisible = query.Length == 0
            || DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || SystemPath.Contains(query, StringComparison.OrdinalIgnoreCase);

    // --- Badges & callouts (10-3). Visible in every display mode; safety-critical
    // information is never hidden by a display preference. ---

    /// <summary>Enforcement badge: the profile's summary, e.g. "Windows is known to revert this setting".</summary>
    public bool HasEnforcementBadge => Model.Enforcement is not null;
    public string? EnforcementSummary => Model.Enforcement?.Summary;
    public string? ReversionRisksText =>
        Model.Enforcement?.ReversionRisks is { Count: > 0 } risks
            ? $"May revert via: {string.Join(", ", risks)}"
            : null;
    public bool HasReversionRisks => ReversionRisksText is not null;

    /// <summary>
    /// SKU callout: informational only; the setting stays toggleable (8-4 rule).
    /// </summary>
    public bool HasSkuNotice { get; }
    public string? SkuNotice { get; }

    /// <summary>
    /// Owner Mode degradation: control visible but inert, card fully readable.
    /// Observable: the card un-degrades live when the service starts.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsControlEnabled))]
    [NotifyPropertyChangedFor(nameof(CanTurnOnOwnerMode))]
    [NotifyPropertyChangedFor(nameof(ShowOwnerModeBadge))]
    private bool _isOwnerModeDegraded;

    public string? OwnerModeCallout { get; private set; }

    /// <summary>Subtle badge when Owner Mode is required AND available.</summary>
    public bool ShowOwnerModeBadge => Model.OwnerModeRequired && !IsOwnerModeDegraded;

    /// <summary>The only thing degradation disables is the control itself.</summary>
    public bool IsControlEnabled => !IsOwnerModeDegraded;

    /// <summary>The callout button needs the lifecycle service to act.</summary>
    public bool CanTurnOnOwnerMode => IsOwnerModeDegraded && _ownerMode is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TurnOnOwnerModeCommand))]
    private bool _isEnablingOwnerMode;

    [ObservableProperty]
    private string? _ownerModeError;

    [RelayCommand(CanExecute = nameof(CanExecuteTurnOnOwnerMode))]
    private async Task TurnOnOwnerModeAsync()
    {
        if (_ownerMode is null)
            return;

        IsEnablingOwnerMode = true;
        OwnerModeError = null;
        try
        {
            var result = await _ownerMode.EnableAsync();
            if (!result.IsSuccess)
                OwnerModeError = result.ErrorMessage ?? "Starting the Owner Mode service failed.";
        }
        finally
        {
            IsEnablingOwnerMode = false;
            RefreshOwnerModeState();
        }
    }

    private bool CanExecuteTurnOnOwnerMode() => !IsEnablingOwnerMode;

    private void OnOwnerModeStateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            RefreshOwnerModeState();
        else
            Dispatcher.UIThread.Post(RefreshOwnerModeState);
    }

    private void RefreshOwnerModeState()
    {
        if (!Model.OwnerModeRequired)
            return;

        var available = _capabilityDetector?.IsOwnerModeAvailable == true;
        IsOwnerModeDegraded = !available;
        if (available)
            OwnerModeError = null;
    }

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

    public SettingCardViewModel(
        SettingCardSource source,
        IPendingChangesService pendingChangesService,
        ICapabilityDetector? capabilityDetector = null,
        IOwnerModeLifecycle? ownerMode = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pendingChangesService);
        _source = source;
        _pendingChangesService = pendingChangesService;
        _capabilityDetector = capabilityDetector;
        Model = source.Model;

        // SKU callout only when the detected edition sits below the minimum tier
        // (IsSkuRestricted handles null/unknown as not-restricted).
        if (capabilityDetector?.IsSkuRestricted(Model.SkuRestriction) == true)
        {
            HasSkuNotice = true;
            var required = Model.SkuRestriction == Core.Modules.WindowsSku.Education
                ? "Enterprise or Education"
                : $"{Model.SkuRestriction} or higher";
            SkuNotice = $"Requires {required}. No effect on this edition.";
        }

        // Owner Mode degradation: no detector means the service can't be reached;
        // treat as unavailable (safe default). The lifecycle event keeps the state
        // live: turning the service on un-degrades every visible card.
        if (Model.OwnerModeRequired)
        {
            _ownerMode = ownerMode;
            OwnerModeCallout = "Needs Owner Mode. The background service keeps this setting applied when Windows reverts it.";
            RefreshOwnerModeState();
            if (_ownerMode is not null)
                _ownerMode.StateChanged += OnOwnerModeStateChanged;
        }

        _registryIsEnabled = Model.CurrentValue == "1";
        _suppressStaging = true;
        IsEnabled = _registryIsEnabled;
        _suppressStaging = false;

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        // Degraded cards must never stage, even via programmatic IsEnabled writes;
        // the disabled ToggleSwitch only blocks UI input.
        if (_suppressStaging || !IsControlEnabled)
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
            Log.Error(ex, "Toggle staging failed for {Setting}", DisplayName);
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
                // Applied; keep toggle position, adopt as new baseline.
                _registryIsEnabled = IsEnabled;
            }
            else
            {
                // Discarded; reset toggle to registry state.
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
        if (_ownerMode is not null)
            _ownerMode.StateChanged -= OnOwnerModeStateChanged;
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }
}
