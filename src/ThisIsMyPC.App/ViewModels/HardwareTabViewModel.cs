using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Modules.Hardware.Models;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// One Hardware tab (System Control, Lighting, Cooling, Monitoring): a status
/// banner with the policy's explanation, the companion button the policy
/// granted, and a closed details block with the evidence. Controls render
/// only when the decision says so; the debug override can show them but
/// never lets them write, because every write path re-checks the decision's
/// permitted operations, not its visibility.
/// </summary>
public sealed partial class HardwareTabViewModel : ViewModelBase, IDisposable
{
    private readonly HardwareCompanionActions? _actions;
    private readonly bool _installAvailable;

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

    /// <param name="installAvailable">The Software module can run installs (winget present). Off hides nothing; it disables Install with a hint.</param>
    public HardwareTabViewModel(
        HardwareTabScanData data,
        HardwareCompanionActions? actions = null,
        Func<Task<OperationResult<HardwareTabScanData>>>? refresh = null,
        bool installAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _actions = actions;
        _installAvailable = installAvailable;
        if (_actions is not null)
            _actions.QueueChanged += OnQueueChanged;
        if (refresh is not null)
            _ = RefreshAsync(refresh);
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

    public string ActionLabel => Decision.Action switch
    {
        { Kind: CompanionActionKind.Install } action => IsInstallQueued
            ? $"{CompanionNames.Of(action.App)} queued for install"
            : $"Install {CompanionNames.Of(action.App)}",
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
        { Kind: CompanionActionKind.Install } when !_installAvailable =>
            "Installs need the app installer (winget), which the Software page reports as unavailable on this PC.",
        { Kind: CompanionActionKind.Install } when IsInstallQueued && string.IsNullOrEmpty(ActionMessage) =>
            "Apply the queued changes to run the install.",
        _ => null,
    };

    public bool HasActionHint => ActionHint is not null;

    [RelayCommand(CanExecute = nameof(CanRunAction))]
    private void RunAction()
    {
        if (Decision.Action is not { } action || _actions is null)
            return;

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

    // ---- controls area ----

    /// <summary>The decision (or the debug override) says the tab renders its controls.</summary>
    public bool ControlsVisible => Decision.ControlsVisible;

    /// <summary>Controls are on screen because of Settings > Advanced, not because they can do anything.</summary>
    public bool IsOverrideShowingControls => Decision.ControlsVisible && !IsAvailable;

    /// <summary>What sits in the controls area in this build, per domain.</summary>
    public string ControlsPlaceholder => Domain switch
    {
        HardwareDomain.Lighting => "Device colors, brightness and modes arrive with the OpenRGB integration. Nothing on this page writes to lighting yet.",
        HardwareDomain.Monitoring => "Sensor readings arrive with the LibreHardwareMonitor integration. Nothing is read yet.",
        HardwareDomain.Cooling => "FanControl runs the fans. Saved cooling presets for this PC arrive in a later build.",
        HardwareDomain.SystemControl => "G-Helper runs the laptop. ThisIsMyPC installs and opens it; it does not replace it.",
        _ => string.Empty,
    };

    // ---- refresh ----

    private async Task RefreshAsync(Func<Task<OperationResult<HardwareTabScanData>>> refresh)
    {
        IsRefreshing = true;
        try
        {
            var result = await refresh().ConfigureAwait(true);
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
        OnPropertyChanged(string.Empty);
        RunActionCommand.NotifyCanExecuteChanged();
    }

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
    }
}
