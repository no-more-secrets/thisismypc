using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Hardware.Detection;
using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Software.Actions;
using ThisIsMyPC.Modules.Software.Services;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// What a Hardware tab may do with a companion app. Install goes through the
/// Software module's pending-actions queue (one-way, no fabricated
/// before-state, applied from the review panel like any other install). Open
/// launches the companion as the signed-in desktop user, never from an
/// elevated token. For Lighting, the bundled OpenRGB is started as a
/// background service instead of opened as a window. None of this touches
/// hardware; the tabs check the policy's permitted operations before calling
/// any of it.
/// </summary>
public sealed class HardwareCompanionActions
{
    private readonly IPendingActionsService? _pendingActions;
    private readonly IInteractiveUserContext? _user;
    private readonly IOpenRgbHost? _lightingHost;

    public HardwareCompanionActions(
        IPendingActionsService? pendingActions = null,
        IInteractiveUserContext? user = null,
        IOpenRgbHost? lightingHost = null)
    {
        _pendingActions = pendingActions;
        _user = user;
        _lightingHost = lightingHost;
        if (_pendingActions is not null)
            _pendingActions.PropertyChanged += (_, _) => QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The pending-actions queue changed (staged, discarded, applied); queued state on the tabs may be stale.</summary>
    public event EventHandler? QueueChanged;

    /// <summary>Catalog id in Software's bundled catalog, or null when the companion is not installable from there.</summary>
    public static string? CatalogIdOf(CompanionApp app) => app switch
    {
        CompanionApp.OpenRgb => "openrgb",
        CompanionApp.FanControl => "fancontrol",
        CompanionApp.GHelper => "g-helper",
        CompanionApp.LibreHardwareMonitor => "librehardwaremonitor",
        CompanionApp.SignalRgb => "signalrgb",
        CompanionApp.HwInfo => "hwinfo",
        _ => null,
    };

    public bool CanInstall(CompanionApp app) => _pendingActions is not null && CatalogIdOf(app) is not null;

    public bool IsInstallQueued(CompanionApp app) =>
        _pendingActions is not null
        && CatalogIdOf(app) is { } id
        && _pendingActions.IsStaged(SoftwareActionFactory.InstallPrefix + id);

    /// <summary>Queues the winget install. It runs when the person applies the review panel.</summary>
    public OperationResult<bool> QueueInstall(CompanionApp app)
    {
        if (_pendingActions is null)
            return OperationResult<bool>.Failure("Installs are not available in this session.", ErrorCategory.ServiceUnavailable);
        var id = CatalogIdOf(app);
        var entry = id is null ? null : SoftwareCatalog.Entries.FirstOrDefault(e => e.Id == id);
        if (entry is null)
            return OperationResult<bool>.Failure($"{CompanionNames.Of(app)} is not in the app catalog.", ErrorCategory.NotFound);

        _pendingActions.Stage(SoftwareActionFactory.CreateInstall(entry));
        return OperationResult<bool>.Success(true);
    }

    public bool CanOpen => _user is not null;

    public OperationResult<bool> Open(string executablePath)
    {
        if (_user is null)
            return OperationResult<bool>.Failure("Opening apps is not available in this session.", ErrorCategory.ServiceUnavailable);
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return OperationResult<bool>.Failure("The program's location could not be found. Open it from the Start menu.", ErrorCategory.NotFound);
        return _user.LaunchAsUser(executablePath);
    }

    // ---- lighting service ----

    /// <summary>A copy of OpenRGB ships with this app, so Lighting runs it as a background service.</summary>
    public bool IsLightingServiceBundled => _lightingHost?.BundledExecutablePath is not null;

    public int LightingPort => _lightingHost?.Port ?? OpenRgbSdkProtocol.DefaultPort;

    public OpenRgbHostState LightingServiceState => _lightingHost?.State ?? OpenRgbHostState.NotBundled;

    /// <summary>Starts the bundled server unless one already answers on the port.</summary>
    public Task<OperationResult<bool>> StartLightingServiceAsync(CancellationToken cancellationToken = default) =>
        _lightingHost is null
            ? Task.FromResult(OperationResult<bool>.Failure("This build ships without the bundled OpenRGB.", ErrorCategory.NotFound))
            : _lightingHost.EnsureRunningAsync(cancellationToken);
}
