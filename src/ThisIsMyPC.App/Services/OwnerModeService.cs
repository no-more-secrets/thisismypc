using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

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
/// Owner Mode service lifecycle (28-2). Enable = register with the SCM
/// (SERVICE_AUTO_START, LocalSystem) + start; disable = stop + start type Disabled
/// (registration stays so re-enabling is one click). State is queried live from the
/// SCM; the service can be managed externally and the UI must not drift from it.
/// </summary>
public sealed class OwnerModeService : IOwnerModeLifecycle
{
    public const string ServiceName = "ThisIsMyPC";
    public const string ServiceDisplayName = "ThisIsMyPC Owner Mode Service";
    public const string ServiceDescription =
        "Detects when Windows reverts ThisIsMyPC-applied settings after updates (drift watchdog).";

    private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(30);

    private readonly IServiceInstaller _installer;
    private readonly IServiceControlService _serviceControl;
    private readonly string _binaryPath;

    public OwnerModeService(
        IServiceInstaller installer, IServiceControlService serviceControl, string? binaryPath = null)
    {
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(serviceControl);
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
        if (start.IsSuccess)
            StateChanged?.Invoke(this, EventArgs.Empty);
        return start;
    }

    public async Task<OperationResult<bool>> DisableAsync(CancellationToken cancellationToken = default)
    {
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
