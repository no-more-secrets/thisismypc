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
/// Display-mode flags (compact, registry visibility) are set by the owning tab VM.
/// </summary>
public sealed partial class SettingCardViewModel : ViewModelBase, IDisposable
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.App.ViewModels.SettingCardViewModel");

    private readonly IPendingChangesService _pendingChangesService;
    private readonly SettingCardSource _source;
    private readonly Core.Policies.PolicyControlStateReader? _policyStates;
    private Core.Policies.PolicyControlState _policyState = Core.Policies.PolicyControlState.None;
    public string? PolicyStateText => IsRegistryDataVisible ? _policyState.Message
        : _policyState.BlocksChanges ? "Controlled by policy" : null;
    public bool HasPolicyState => PolicyStateText is not null;
    private readonly ICapabilityDetector? _capabilityDetector;
    private readonly IOwnerModeLifecycle? _ownerMode;
    private bool _registryIsEnabled;
    private bool _suppressStaging;
    private bool _isStagingChange;
    private bool _disposed;
    private string? _stagedGroupId;
    private bool _stagedToggleState;
    private CancellationTokenSource? _debounceCts;

    public SettingCardModel Model { get; }

    public string DisplayName => Model.DisplayName;
    public string Description => Model.Description;
    public bool IsToggle => Model.ControlType == SettingControlType.Toggle;
    public bool IsDropdown => Model.ControlType == SettingControlType.Dropdown;
    public IReadOnlyList<SettingOption> Options => Model.AvailableOptions ?? [];
    public string CurrentChoiceDisplay => Model.CurrentDisplayValue ?? "Unknown";
    private string _choiceBaseline = "";
    private SettingOption? _stagedChoice;

    [ObservableProperty]
    private SettingOption? _selectedOption;

    partial void OnSelectedOptionChanged(SettingOption? value)
    {
        if (_suppressStaging || !IsDropdown || value is null) return;
        try
        {
            RefreshPolicyState();
            if (!IsControlEnabled || !Options.Contains(value))
                throw new InvalidOperationException(_policyState.Message ?? "This option is unavailable.");
            var current = _source.ReadCurrentValue!();
            var group = _source.CreateChoiceGroup!(value.Value);
            _choiceBaseline = current;
            _isStagingChange = true;
            try
            {
                if (_stagedGroupId is not null) _pendingChangesService.Unstage(_stagedGroupId);
                _stagedGroupId = null;
                if (value.Value != current)
                {
                    _pendingChangesService.Stage(group);
                    _stagedGroupId = group.GroupId;
                    _stagedChoice = value;
                }
            }
            finally { _isStagingChange = false; }
            UpdatePendingState();
        }
        catch (Exception ex)
        {
            _suppressStaging = true;
            SelectedOption = _stagedGroupId is null ? Options.FirstOrDefault(o => o.Value == _choiceBaseline) : _stagedChoice;
            _suppressStaging = false;
            UpdatePendingState();
            _feedback?.Fail($"Could not change {DisplayName}: {ex.Message}");
        }
    }
    public string SystemPath => Model.RegistryPath is null
        ? string.Empty
        : Model.ValueName is null ? Model.RegistryPath : $@"{Model.RegistryPath}\{Model.ValueName}";

    /// <summary>
    /// The (i) tooltip: the description, like every other module's card. In
    /// Compact mode the informational lines the card no longer shows (how the
    /// setting is applied, what may revert it) follow it, so nothing is lost.
    /// </summary>
    public string TooltipText
    {
        get
        {
            var lines = new List<string> { Description };
            if (_policyState.Message is { Length: > 0 } policy) lines.Add(policy);
            if (!IsCompact) return string.Join(Environment.NewLine, lines);
            if (EnforcementSummary is { Length: > 0 } summary)
                lines.Add(summary);
            if (ReversionRisksText is { } risks)
                lines.Add(risks);
            return string.Join(Environment.NewLine, lines);
        }
    }

    // --- Technical details: registry path, value type, and the reading at scan
    // time. Hidden by default; the page's Technical details box opens them on every card. ---

    /// <summary>The path with a break opportunity after each separator, so a long key wraps between names instead of inside one.</summary>
    public string WrappableSystemPath => SystemPath.Replace("\\", "\\​", StringComparison.Ordinal);

    /// <summary>The content slot has something to show; hidden otherwise so a compact card stays one line tall.</summary>
    public bool HasVisibleContent =>
        IsDropdown || ShowEnforcementBadge || ShowReversionRisks || HasSkuNotice || HasUnavailableReason || HasPolicyState || IsOwnerModeDegraded
        || ShowOwnerModeBadge || IsRegistryDataVisible;

    /// <summary>"DWord value, read as Suppressed at the last scan".</summary>
    public string TechnicalStateText
    {
        get
        {
            var type = Model.RegistryValueType?.Replace("Registry_", string.Empty, StringComparison.Ordinal);
            var typePart = string.IsNullOrEmpty(type) ? "Value" : $"{type} value";
            return string.IsNullOrEmpty(Model.CurrentDisplayValue)
                ? typePart
                : $"{typePart}, read as {Model.CurrentDisplayValue} at the last scan";
        }
    }

    /// <summary>Card templates bind their root visibility here; the owning tab's search sets it.</summary>
    [ObservableProperty]
    private bool _isSearchVisible = true;

    public void ApplySearch(string query) =>
        IsSearchVisible = query.Length == 0
            || DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || SystemPath.Contains(query, StringComparison.OrdinalIgnoreCase);

    // --- Badges & callouts (10-3). The safety-critical ones (SKU restriction,
    // Owner Mode degradation) show in every display mode. The informational
    // ones (how the setting is applied, what may revert it) leave the card in
    // Compact mode and move into the (i) tooltip instead. ---

    /// <summary>Enforcement badge: the profile's summary, e.g. "Windows is known to revert this setting".</summary>
    public bool HasEnforcementBadge => Model.Enforcement is not null;
    public string? EnforcementSummary => Model.Enforcement?.Summary;
    public string? ReversionRisksText =>
        Model.Enforcement?.ReversionRisks is { Count: > 0 } risks
            ? $"May revert via: {string.Join(", ", risks)}"
            : null;
    public bool HasReversionRisks => ReversionRisksText is not null;

    public bool ShowEnforcementBadge => HasEnforcementBadge && !IsCompact;
    public bool ShowReversionRisks => HasReversionRisks && !IsCompact;

    /// <summary>
    /// Edition requirements stay visible in every display mode.
    /// </summary>
    public bool HasSkuNotice { get; }
    public bool IsEditionBlocked { get; }
    public string? SkuNotice { get; }

    /// <summary>
    /// Owner Mode degradation: control visible but inert, card fully readable.
    /// Observable: the card un-degrades live when the service starts.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsControlEnabled))]
    [NotifyPropertyChangedFor(nameof(CanTurnOnOwnerMode))]
    [NotifyPropertyChangedFor(nameof(ShowOwnerModeBadge))]
    [NotifyPropertyChangedFor(nameof(HasVisibleContent))]
    private bool _isOwnerModeDegraded;

    public string? OwnerModeCallout { get; private set; }

    /// <summary>Subtle badge when Owner Mode is required AND available; informational, so Compact folds it away.</summary>
    public bool ShowOwnerModeBadge => Model.OwnerModeRequired && !IsOwnerModeDegraded && !IsCompact;

    public bool HasUnavailableReason => Model.UnavailableReason is not null;
    public string? UnavailableReason => Model.UnavailableReason;
    /// <summary>Keep the card readable when its control cannot safely change the setting.</summary>
    public bool IsControlEnabled => !IsOwnerModeDegraded && !HasUnavailableReason && !IsEditionBlocked && !_policyState.BlocksChanges;

    /// <summary>The callout button needs the lifecycle service to act.</summary>
    public bool CanTurnOnOwnerMode => IsOwnerModeDegraded && _ownerMode is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TurnOnOwnerModeCommand))]
    private bool _isEnablingOwnerMode;

    /// <summary>Why the last Turn on Owner Mode click failed; the card shows it as an icon, the status line as text.</summary>
    [ObservableProperty]
    private string? _ownerModeError;

    private readonly Services.IUserFeedback? _feedback;

    partial void OnOwnerModeErrorChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _feedback?.Fail(value);
    }

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

    /// <summary>
    /// Compact display: the card keeps its title, (i), switch, and the
    /// safety callouts; the informational badge lines move into the tooltip.
    /// Set by the owning page.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TooltipText))]
    [NotifyPropertyChangedFor(nameof(ShowEnforcementBadge))]
    [NotifyPropertyChangedFor(nameof(ShowReversionRisks))]
    [NotifyPropertyChangedFor(nameof(ShowOwnerModeBadge))]
    [NotifyPropertyChangedFor(nameof(HasVisibleContent))]
    private bool _isCompact;

    /// <summary>The technical details panel; closed by default, opened by the page's Technical details box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVisibleContent))]
    [NotifyPropertyChangedFor(nameof(PolicyStateText))]
    [NotifyPropertyChangedFor(nameof(HasPolicyState))]
    private bool _isRegistryDataVisible;

    public SettingCardViewModel(
        SettingCardSource source,
        IPendingChangesService pendingChangesService,
        ICapabilityDetector? capabilityDetector = null,
        IOwnerModeLifecycle? ownerMode = null,
        Services.IUserFeedback? feedback = null,
        Core.Policies.PolicyControlStateReader? policyStates = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pendingChangesService);
        _source = source;
        _pendingChangesService = pendingChangesService;
        _capabilityDetector = capabilityDetector;
        _feedback = feedback;
        Model = source.Model;
        _policyStates = policyStates;
        RefreshPolicyState();

        var required = SettingEditionSupport.RequiredEdition(SystemPath, Model.SkuRestriction);
        var editionReason = SettingEditionSupport.BlockReason(capabilityDetector?.Sku, SystemPath, Model.SkuRestriction);
        IsEditionBlocked = editionReason is not null;
        SkuNotice = editionReason ?? SettingEditionSupport.RequirementLabel(required);
        HasSkuNotice = SkuNotice is not null;

        // Owner Mode degradation: no detector means the service can't be reached;
        // treat as unavailable (safe default). The lifecycle event keeps the state
        // live: turning the service on un-degrades every visible card.
        if (Model.OwnerModeRequired)
        {
            _ownerMode = ownerMode;
            OwnerModeCallout = "Needs Owner Mode. The background service reports when Windows reverts this setting; you reapply it from Home.";
            RefreshOwnerModeState();
            if (_ownerMode is not null)
                _ownerMode.StateChanged += OnOwnerModeStateChanged;
        }

        _registryIsEnabled = _policyState.ToggleState ?? Model.CurrentValue == "1";
        _suppressStaging = true;
        IsEnabled = _policyState.ToggleState ?? _registryIsEnabled;
        _choiceBaseline = Model.CurrentValue;
        SelectedOption = Options.FirstOrDefault(option => option.Value == _choiceBaseline);
        _suppressStaging = false;

        _pendingChangesService.PropertyChanged += OnPendingChangesPropertyChanged;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        // Degraded cards must never stage, even via programmatic IsEnabled writes;
        // the disabled ToggleSwitch only blocks UI input.
        if (_suppressStaging || !IsToggle)
            return;

        RefreshPolicyState();
        if (!IsControlEnabled)
        {
            _suppressStaging = true;
            IsEnabled = _policyState.ToggleState ?? _registryIsEnabled;
            _suppressStaging = false;
            return;
        }

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

            RefreshPolicyState();
            if (!IsControlEnabled)
            {
                _suppressStaging = true;
                IsEnabled = _policyState.ToggleState ?? _registryIsEnabled;
                _suppressStaging = false;
                return;
            }

            var currentState = _policyState.ToggleState ?? _source.ReadCurrentState();

            var group = _source.CreateToggleGroup(desiredState);
            group = _policyStates?.PrepareToggleGroup(group, desiredState) ?? group;
            _registryIsEnabled = currentState;

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
                    _stagedToggleState = desiredState;
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
            _suppressStaging = true;
            IsEnabled = _stagedGroupId is not null ? _stagedToggleState : _registryIsEnabled;
            _suppressStaging = false;
            UpdatePendingState();
            _feedback?.Fail($"Could not change {DisplayName}: {ex.Message}");
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
            var applied = _pendingChangesService.WasApplied(_stagedGroupId);
            _stagedGroupId = null;

            if (IsDropdown)
            {
                if (applied) _choiceBaseline = SelectedOption?.Value ?? _choiceBaseline;
                _suppressStaging = true;
                SelectedOption = Options.FirstOrDefault(option => option.Value == _choiceBaseline);
                _suppressStaging = false;
                RefreshPolicyState();
                UpdatePendingState();
                return;
            }

            if (applied)
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
        if (_stagedGroupId is null && Core.Policies.LocalPolicyToggleCatalog.Location(Model.ModuleId, Model.SettingId) is not null)
        {
            RefreshPolicyState();
            _registryIsEnabled = _policyState.ToggleState ?? _source.ReadCurrentState();
            _suppressStaging = true;
            IsEnabled = _registryIsEnabled;
            _suppressStaging = false;
        }
    }

    private void UpdatePendingState()
    {
        HasPendingChange = _stagedGroupId is not null;
        IsPendingEnable = HasPendingChange && (IsDropdown || IsEnabled);
        IsPendingDisable = HasPendingChange && IsToggle && !IsEnabled;
    }

    private void RefreshPolicyState()
    {
        _policyState = _source.ReadPolicyState?.Invoke() ?? _policyStates?.Read(Model.ModuleId, Model.SettingId, SystemPath,
            Enum.TryParse<Core.Changes.ChangeValueType>(Model.RegistryValueType, out var type) ? type : null) ?? Core.Policies.PolicyControlState.None;
        OnPropertyChanged(nameof(PolicyStateText));
        OnPropertyChanged(nameof(HasPolicyState));
        OnPropertyChanged(nameof(HasVisibleContent));
        OnPropertyChanged(nameof(IsControlEnabled));
        OnPropertyChanged(nameof(TooltipText));
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
