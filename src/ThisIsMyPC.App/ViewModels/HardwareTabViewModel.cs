using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Modules.Hardware.Models;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// One Hardware tab (System Control, Lighting, Cooling, Monitoring): a status
/// banner with the policy's explanation, the companion button the policy
/// granted, and a closed details block with the evidence. Controls render
/// only when the decision says so; the debug override can show them but
/// never lets them write, because every write path re-checks the decision's
/// permitted operations, not its visibility. Lighting starts the bundled
/// OpenRGB service when the page opens and hosts the device controls.
/// </summary>
public sealed partial class HardwareTabViewModel : ViewModelBase, IDisposable
{
    private readonly HardwareCompanionActions? _actions;
    private readonly bool _installAvailable;
    private readonly Func<Task<OperationResult<HardwareTabScanData>>>? _refresh;
    private readonly IOpenRgbClient? _lightingClient;
    private bool _serviceStartAttempted;

    [ObservableProperty]
    private HardwareTabScanData _data;

    /// <summary>True while a fresh detection pass runs behind the page.</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>Outcome of the last companion action, shown under the button.</summary>
    [ObservableProperty]
    private string? _actionMessage;

    [ObservableProperty]
    private bool _actionFailed;

    /// <summary>The Lighting device controls; null on the other tabs and while Lighting cannot show controls.</summary>
    [ObservableProperty]
    private LightingControlsViewModel? _lighting;

    /// <param name="installAvailable">The Software module can run installs (winget present). Off hides nothing; it disables Install with a hint.</param>
    /// <param name="refreshOnOpen">Run <paramref name="refresh"/> behind the page right away (the snapshot shown is old).</param>
    public HardwareTabViewModel(
        HardwareTabScanData data,
        HardwareCompanionActions? actions = null,
        Func<Task<OperationResult<HardwareTabScanData>>>? refresh = null,
        bool installAvailable = true,
        bool refreshOnOpen = true,
        IOpenRgbClient? lightingClient = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _actions = actions;
        _installAvailable = installAvailable;
        _refresh = refresh;
        _lightingClient = lightingClient;
        if (_actions is not null)
            _actions.QueueChanged += OnQueueChanged;
        SyncLighting();
        if (!TryAutoStartLightingService() && refresh is not null && refreshOnOpen)
            _ = RefreshAsync();
    }

    public HardwareTabDecision Decision => Data.Decision;

    public HardwareDomain Domain => Decision.Domain;

    public string Explanation => Decision.Explanation;

    public HardwareAvailability Availability => Decision.Availability;

    public bool IsAvailable => Availability == HardwareAvailability.Available;
    public bool IsUnavailable => Availability == HardwareAvailability.Unavailable;
    public bool IsUnknown => Availability == HardwareAvailability.Unknown;
    public bool IsPendingVerification => Availability == HardwareAvailability.PendingVerification;
    public bool IsConflict => Availability == HardwareAvailability.Conflict;

    public string StatusLabel => Availability switch
    {
        HardwareAvailability.Available => "Available",
        HardwareAvailability.Unavailable => "Not available",
        HardwareAvailability.Unknown => "Unknown",
        HardwareAvailability.PendingVerification => "Not verified",
        HardwareAvailability.Conflict => "In use elsewhere",
        _ => Availability.ToString(),
    };

    /// <summary>Manufacturer, model and form factor on one line.</summary>
    public string MachineLine
    {
        get
        {
            var identity = Data.Report.Identity;
            var name = identity.Manufacturer is { } manufacturer
                ? identity.Model is { } model ? $"{manufacturer} {model}" : manufacturer
                : "Unknown manufacturer";
            var formFactor = Data.Report.FormFactor.FormFactor switch
            {
                MachineFormFactor.Desktop => "desktop",
                MachineFormFactor.Laptop => "laptop",
                _ => "form factor unknown",
            };
            return $"{name}, {formFactor}";
        }
    }

    public string CheckedAt => $"Checked {Data.ObservedAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}";

    public IReadOnlyList<string> Evidence => Decision.Evidence;
    public IReadOnlyList<string> ConflictNotes => Decision.ConflictNotes;
    public bool HasConflictNotes => ConflictNotes.Count > 0;
    public IReadOnlyList<string> DetectionNotes => Data.DetectionNotes;

    // ---- companion action ----

    public bool HasAction => Decision.Action is not null;

    /// <summary>The policy asks for the bundled lighting service to be started (no window to open).</summary>
    private bool StartsLightingService => Decision.Action is { Kind: CompanionActionKind.StartService };

    public string ActionLabel => Decision.Action switch
    {
        { Kind: CompanionActionKind.Install } action => IsInstallQueued
            ? $"{CompanionNames.Of(action.App)} queued for install"
            : $"Install {CompanionNames.Of(action.App)}",
        { Kind: CompanionActionKind.StartService } => "Start lighting service",
        { Kind: CompanionActionKind.Open } action => $"Open {CompanionNames.Of(action.App)}",
        _ => string.Empty,
    };

    public bool IsInstallQueued =>
        Decision.Action is { Kind: CompanionActionKind.Install, App: var app } && _actions?.IsInstallQueued(app) == true;

    /// <summary>The policy granted the operation and the session can carry it out.</summary>
    public bool CanRunAction => Decision.Action switch
    {
        { Kind: CompanionActionKind.Install, App: var app } =>
            Decision.Operations.HasFlag(HardwareOperations.InstallCompanion)
            && _installAvailable && _actions?.CanInstall(app) == true && !IsInstallQueued,
        { Kind: CompanionActionKind.StartService } =>
            Decision.Operations.HasFlag(HardwareOperations.OpenCompanion)
            && _actions?.IsLightingServiceBundled == true && !IsRefreshing,
        { Kind: CompanionActionKind.Open } =>
            Decision.Operations.HasFlag(HardwareOperations.OpenCompanion)
            && _actions?.CanOpen == true && Data.LaunchPath is { Length: > 0 },
        _ => false,
    };

    /// <summary>Why the button is disabled, when it is. The action message says it instead right after a click.</summary>
    public string? ActionHint => Decision.Action switch
    {
        { Kind: CompanionActionKind.Open } when Data.LaunchPath is null or "" =>
            "Its location could not be found. Open it from the Start menu.",
        { Kind: CompanionActionKind.StartService } when _actions?.IsLightingServiceBundled != true =>
            "This build ships without the bundled OpenRGB.",
        { Kind: CompanionActionKind.Install } when !_installAvailable =>
            "Installs need the app installer (winget), which the Software page reports as unavailable on this PC.",
        { Kind: CompanionActionKind.Install } when IsInstallQueued && string.IsNullOrEmpty(ActionMessage) =>
            "Apply the queued changes to run the install.",
        _ => null,
    };

    public bool HasActionHint => ActionHint is not null;

    [RelayCommand(CanExecute = nameof(CanRunAction))]
    private async Task RunActionAsync()
    {
        if (Decision.Action is not { } action || _actions is null)
            return;

        if (StartsLightingService && Decision.Operations.HasFlag(HardwareOperations.OpenCompanion))
        {
            await StartLightingServiceAsync().ConfigureAwait(true);
            return;
        }

        // The visibility override never reaches here: CanRunAction reads the
        // permitted operations, which the override cannot change.
        var result = action.Kind switch
        {
            CompanionActionKind.Install when Decision.Operations.HasFlag(HardwareOperations.InstallCompanion)
                => _actions.QueueInstall(action.App),
            CompanionActionKind.Open when Decision.Operations.HasFlag(HardwareOperations.OpenCompanion)
                => _actions.Open(Data.LaunchPath ?? string.Empty),
            _ => OperationResult<bool>.Failure("This action is not permitted here.", ErrorCategory.AccessDenied),
        };

        ActionFailed = !result.IsSuccess;
        ActionMessage = result.IsSuccess
            ? action.Kind == CompanionActionKind.Install
                ? $"{CompanionNames.Of(action.App)} is queued. Apply the queued changes to install it."
                : $"{CompanionNames.Of(action.App)} is opening."
            : result.ErrorMessage;
        NotifyActionState();
    }

    /// <summary>The queue changed elsewhere (discarded, applied): the button and its label follow it.</summary>
    private void OnQueueChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            NotifyActionState();
        else
            Dispatcher.UIThread.Post(NotifyActionState);
    }

    // ---- lighting service ----

    /// <summary>
    /// The policy asked for the bundled lighting service (no OpenRGB running
    /// at all): start it as the page opens, once, unless another program owns
    /// the devices (Conflict). A user's own OpenRGB running without its SDK
    /// server never gets this action, so no second server is started beside
    /// it. Returns true when a start was kicked off, which also refreshes the page.
    /// </summary>
    private bool TryAutoStartLightingService()
    {
        if (_serviceStartAttempted || !StartsLightingService || IsConflict
            || _actions?.IsLightingServiceBundled != true
            || !Decision.Operations.HasFlag(HardwareOperations.OpenCompanion))
        {
            return false;
        }
        _ = StartLightingServiceAsync();
        return true;
    }

    private async Task StartLightingServiceAsync()
    {
        if (_actions is null)
            return;
        _serviceStartAttempted = true;
        IsRefreshing = true;
        ActionFailed = false;
        ActionMessage = "Starting the lighting service...";
        NotifyActionState();
        try
        {
            var start = await _actions.StartLightingServiceAsync().ConfigureAwait(true);
            if (!start.IsSuccess)
            {
                ActionFailed = true;
                ActionMessage = start.ErrorMessage;
                return;
            }
            ActionMessage = null;
            if (_refresh is not null)
            {
                var result = await _refresh().ConfigureAwait(true);
                if (result.IsSuccess && result.Value is { } fresh)
                    Apply(fresh);
            }
        }
        finally
        {
            IsRefreshing = false;
            NotifyActionState();
        }
    }

    // ---- controls area ----

    /// <summary>The decision (or the debug override) says the tab renders its controls.</summary>
    public bool ControlsVisible => Decision.ControlsVisible;

    /// <summary>Controls are on screen because of Settings > Advanced, not because they can do anything.</summary>
    public bool IsOverrideShowingControls => Decision.ControlsVisible && !IsAvailable;

    /// <summary>Lighting renders its device controls; the placeholder is for the other tabs and for Lighting without a client.</summary>
    public bool ShowsPlaceholder => Lighting is null;

    /// <summary>What sits in the controls area in this build, per domain.</summary>
    public string ControlsPlaceholder => Domain switch
    {
        HardwareDomain.Lighting => "Lighting devices appear here once the lighting service answers.",
        HardwareDomain.Monitoring => "Sensor readings arrive with the LibreHardwareMonitor integration. Nothing is read yet.",
        HardwareDomain.Cooling => "FanControl runs the fans. Saved cooling presets for this PC arrive in a later build.",
        HardwareDomain.SystemControl => "G-Helper runs the laptop. ThisIsMyPC installs and opens it; it does not replace it.",
        _ => string.Empty,
    };

    /// <summary>Creates or drops the Lighting controls to match the decision; writes stay gated by the live decision.</summary>
    private void SyncLighting()
    {
        if (Domain != HardwareDomain.Lighting || _lightingClient is null || !ControlsVisible)
        {
            Lighting?.Dispose();
            Lighting = null;
            return;
        }
        if (Lighting is null)
        {
            Lighting = new LightingControlsViewModel(
                _lightingClient,
                _actions?.LightingPort ?? Core.Hardware.Detection.OpenRgbSdkProtocol.DefaultPort,
                () => Decision.LiveWritesAllowed);
            _ = Lighting.LoadAsync();
        }
        else
        {
            Lighting.WritesAllowed = Decision.LiveWritesAllowed;
            if (IsAvailable && !Lighting.HasDevices)
                _ = Lighting.LoadAsync();
        }
    }

    // ---- refresh ----

    private async Task RefreshAsync()
    {
        if (_refresh is null)
            return;
        IsRefreshing = true;
        try
        {
            var result = await _refresh().ConfigureAwait(true);
            if (result.IsSuccess && result.Value is { } fresh)
                Apply(fresh);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>Replaces the page's data with a newer decision. Public for tests.</summary>
    public void Apply(HardwareTabScanData fresh)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        Data = fresh;
    }

    /// <summary>Every displayed value derives from Data, so a new Data refreshes them all.</summary>
    partial void OnDataChanged(HardwareTabScanData value)
    {
        SyncLighting();
        OnPropertyChanged(string.Empty);
        RunActionCommand.NotifyCanExecuteChanged();
    }

    partial void OnLightingChanged(LightingControlsViewModel? value) => OnPropertyChanged(nameof(ShowsPlaceholder));

    partial void OnIsRefreshingChanged(bool value) => RunActionCommand.NotifyCanExecuteChanged();

    private void NotifyActionState()
    {
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(IsInstallQueued));
        OnPropertyChanged(nameof(CanRunAction));
        OnPropertyChanged(nameof(ActionHint));
        OnPropertyChanged(nameof(HasActionHint));
        RunActionCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_actions is not null)
            _actions.QueueChanged -= OnQueueChanged;
        Lighting?.Dispose();
        Lighting = null;
    }
}
