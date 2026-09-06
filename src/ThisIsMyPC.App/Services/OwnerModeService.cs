using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift.Consent;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.App.Services;

public enum OwnerModeState
{
    NotInstalled,
    Stopped,
    Disabled,
    Running,
    /// <summary>SCM query failed for a reason other than "does not exist"; state honest-unknown, not "not installed".</summary>
    Unknown,
}

/// <summary>
/// SCM lifecycle and explicit restoration consent remain separate.
/// Enable requires a service acknowledgement; Pause saves consent-off before SCM shutdown.
/// The service process alone never grants the restoration capability.
/// </summary>
public sealed class OwnerModeService : IOwnerModeServiceControl
{
    public const string ServiceName = "ThisIsMyPC";
    public const string ServiceDisplayName = "ThisIsMyPC Owner Mode Service";
    public const string ServiceDescription =
        "Reports changes to saved settings and supports explicitly authorized restoration.";

    private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(30);

    private readonly IServiceInstaller _installer;
    private readonly IServiceControlService _serviceControl;
    private readonly string _binaryPath;
    private readonly IMutationLeaseProvider? _mutationLeaseProvider;
    private readonly IMachineConsentStore? _consentStore;
    private readonly IIpcClient? _ipc;
    private bool _restorationEnabled;
    public bool IsRestorationEnabled => Volatile.Read(ref _restorationEnabled) && IsRunning;

    public OwnerModeService(
        IServiceInstaller installer, IServiceControlService serviceControl, string? binaryPath = null,
        IMutationLeaseProvider? mutationLeaseProvider = null, IMachineConsentStore? consentStore = null,
        IIpcClient? ipc = null)
    {
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(serviceControl);
        if ((mutationLeaseProvider is null) != (consentStore is null))
            throw new ArgumentException("Pause requires both a mutation lease provider and a consent store.");
        _mutationLeaseProvider = mutationLeaseProvider;
        _consentStore = consentStore;
        _ipc = ipc;
        _installer = installer;
        _serviceControl = serviceControl;
        _binaryPath = binaryPath ?? Path.Combine(AppContext.BaseDirectory, "ThisIsMyPC.Service.exe");
    }

    /// <summary>Raised after an enable/disable completes so capability-dependent UI can refresh.</summary>
    public event EventHandler? StateChanged;

    public OwnerModeState GetState()
    {
        var status = _serviceControl.Query(ServiceName);
        if (!status.IsSuccess)
        {
            return status.ErrorCategory == ErrorCategory.NotFound
                ? OwnerModeState.NotInstalled
                : OwnerModeState.Unknown; // e.g. SCM access denied; never claim "not installed"
        }
        return status.Value!.State == ServiceState.Running
            ? OwnerModeState.Running
            : status.Value.StartType == ServiceStartType.Disabled
                ? OwnerModeState.Disabled
                : OwnerModeState.Stopped;
    }

    // Card builds probe per card; a short memo keeps that from turning into one SCM
    // round-trip per rendered card. Invalidated by enable/disable transitions.
    private static readonly TimeSpan ProbeCacheTtl = TimeSpan.FromSeconds(2);
    private readonly Lock _probeLock = new();
    private bool _cachedIsRunning;
    private long _probeExpiresAt;

    /// <summary>Owner Mode capability probe; true only with a live service.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_probeLock)
            {
                if (Environment.TickCount64 < _probeExpiresAt)
                    return _cachedIsRunning;
                _cachedIsRunning = GetState() == OwnerModeState.Running;
                _probeExpiresAt = Environment.TickCount64 + (long)ProbeCacheTtl.TotalMilliseconds;
                return _cachedIsRunning;
            }
        }
    }

    private void InvalidateProbe()
    {
        lock (_probeLock)
            _probeExpiresAt = 0;
    }

    public async Task<OperationResult<bool>> EnableAsync(CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _restorationEnabled, false);
        if (_ipc is null)
            return OperationResult<bool>.Failure("Trusted restoration is unavailable in this build.", ErrorCategory.ServiceUnavailable);
        if (!File.Exists(_binaryPath))
        {
            return OperationResult<bool>.Failure(
                $"Service binary not found at {_binaryPath}. Reinstall ThisIsMyPC to restore it.",
                ErrorCategory.NotFound);
        }

        var install = _installer.Install(ServiceName, ServiceDisplayName, ServiceDescription, _binaryPath);
        if (!install.IsSuccess)
            return install;

        // A previous disable leaves start type Disabled; restore auto-start before starting.
        var startType = _serviceControl.SetStartType(ServiceName, ServiceStartType.Automatic);
        if (!startType.IsSuccess)
            return startType;

        var start = await _serviceControl.StartAsync(ServiceName, ControlTimeout, cancellationToken)
            .ConfigureAwait(false);
        InvalidateProbe();
        if (!start.IsSuccess) return start;
        var enabled = await _ipc.EnableRestorationAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _restorationEnabled, enabled.IsSuccess && enabled.Value is { State: RestorationServiceState.Enabled, ConsentGranted: true });
        StateChanged?.Invoke(this, EventArgs.Empty);
        return enabled.IsSuccess && enabled.Value is { State: RestorationServiceState.Enabled, ConsentGranted: true }
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure(enabled.Value?.Detail ?? enabled.ErrorMessage ?? "Restoration was not enabled.", ErrorCategory.ServiceUnavailable);
    }

    public async Task<RestorationStatusResponse> GetRestorationStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_ipc is null || GetState() != OwnerModeState.Running)
        {
            Volatile.Write(ref _restorationEnabled, false);
            return ReadLocalConsentStatus();
        }
        var result = await _ipc.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var status = result.IsSuccess && result.Value?.ProtocolVersion == IpcProtocol.ProtocolVersion
            ? result.Value.Restoration ?? new() : ReadLocalConsentStatus() with { Detail = result.ErrorMessage ?? "Service status is unavailable or incompatible." };
        Volatile.Write(ref _restorationEnabled, status is { State: RestorationServiceState.Enabled, ConsentGranted: true });
        return status;
    }

    private RestorationStatusResponse ReadLocalConsentStatus()
    {
        var consent = _consentStore?.Read();
        return consent is { Status: MachineConsentStatus.Loaded, Enabled: false }
            ? new() { State = RestorationServiceState.Paused, Detail = "Restoration is paused." }
            : new() { ConsentGranted = consent?.IsGranted == true };
    }

    public async Task<OperationResult<bool>> DisableAsync(CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _restorationEnabled, false);
        if (_mutationLeaseProvider is null && _ipc is not null)
        {
            var paused = await _ipc.PauseRestorationAsync(cancellationToken).ConfigureAwait(false);
            if (!paused.IsSuccess || paused.Value is not { State: RestorationServiceState.Paused, ConsentGranted: false })
                return OperationResult<bool>.Failure(paused.Value?.Detail ?? paused.ErrorMessage ?? "Pause was not confirmed.", ErrorCategory.ServiceUnavailable);
        }
        if (_mutationLeaseProvider is not null)
        {
            // Opt-out does not need journal recovery. It remains possible when recovery is corrupt.
            var acquisition = await _mutationLeaseProvider.AcquireAsync(ControlTimeout, cancellationToken).ConfigureAwait(false);
            if (!acquisition.IsAcquired)
                return OperationResult<bool>.Failure("Owner Mode could not acquire the lease to pause.", ErrorCategory.ServiceUnavailable);
            await using (var lease = acquisition.Lease!)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var off = _consentStore!.SetEnabled(false, lease);
                if (!off.IsSuccess || off.State.Status != MachineConsentStatus.Loaded || off.State.Enabled)
                    return OperationResult<bool>.Failure("Owner Mode could not save paused consent: " + off.State.Detail, ErrorCategory.ServiceUnavailable);
            }
        }
        // SCM stop must happen after release, so a service waiting for the lease can shut down.
        var state = GetState();
        if (state == OwnerModeState.NotInstalled)
            return OperationResult<bool>.Success(true);

        if (state == OwnerModeState.Running)
        {
            var stop = await _serviceControl.StopAsync(ServiceName, ControlTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (!stop.IsSuccess)
                return stop;
        }

        var result = _serviceControl.SetStartType(ServiceName, ServiceStartType.Disabled);
        InvalidateProbe();
        if (result.IsSuccess)
            StateChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }
}
