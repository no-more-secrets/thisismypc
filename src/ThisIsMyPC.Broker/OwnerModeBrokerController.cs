using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift.Consent;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Win32.Coordination;
using ThisIsMyPC.Interop.Win32.Drift;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Interop.Win32.Drift.Consent;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Services;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Broker;

internal sealed class OwnerModeBrokerController
{
    private const string ServiceName = "ThisIsMyPC";
    private const string DisplayName = "ThisIsMyPC Owner Mode Service";
    private const string Description = "Reports changes to saved settings and supports explicitly authorized restoration.";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly IServiceInstaller _installer;
    private readonly IServiceControlService _serviceControl;
    private readonly IIpcClient _ipc;
    private readonly IMutationLeaseProvider _leases;
    private readonly IMachineConsentStore _consent;
    private readonly string? _uiUserSid;

    internal OwnerModeBrokerController(IServiceInstaller? installer = null,
        IServiceControlService? serviceControl = null, IIpcClient? ipc = null,
        IMutationLeaseProvider? leases = null, IMachineConsentStore? consent = null, string? uiUserSid = null)
    {
        _uiUserSid = uiUserSid;
        _installer = installer ?? new ServiceInstaller();
        _serviceControl = serviceControl ?? new ServiceControlService();
        _ipc = ipc ?? new IpcClient();
        _leases = leases ?? new NamedMutexMutationLeaseProvider(MutationLeaseNames.Production, "owner-mode-pause");
        _consent = consent ?? new MachineConsentStore(leaseName: _leases.Name);
    }

    internal async Task<OperationResult<bool>> Enable(CancellationToken cancellationToken)
    {
        var binaryPath = Path.Combine(AppContext.BaseDirectory, "ThisIsMyPC.Service.exe");
        if (!File.Exists(binaryPath))
            return OperationResult<bool>.Failure("The Owner Mode service binary is missing.", ErrorCategory.NotFound);

        if (string.IsNullOrWhiteSpace(_uiUserSid))
            return OperationResult<bool>.Failure("The verified UI account is unavailable.", ErrorCategory.AccessDenied);
        using (var restoration = new NativeRestorationSession(new RegistryService(), primaryUserSid: _uiUserSid))
        {
            var readiness = await restoration.Coordinator.RunAsync(Timeout,
                (lease, _) => Task.FromResult(restoration.Baseline.Read(lease).Count > 0), cancellationToken).ConfigureAwait(false);
            if (!readiness.OperationRan || !readiness.Value)
                return OperationResult<bool>.Failure(readiness.Recovery?.ErrorMessage
                    ?? "Apply a supported Annoyances setting before enabling Owner Mode.", ErrorCategory.ServiceUnavailable);
        }

        var installed = _installer.Install(ServiceName, DisplayName, Description, binaryPath);
        if (!installed.IsSuccess)
            return installed;
        var startType = _serviceControl.SetStartType(ServiceName, ServiceStartType.Automatic);
        if (!startType.IsSuccess)
            return startType;
        var started = await _serviceControl.StartAsync(ServiceName, Timeout, cancellationToken).ConfigureAwait(false);
        if (!started.IsSuccess)
            return started;
        var enabled = await _ipc.EnableRestorationAsync(cancellationToken).ConfigureAwait(false);
        return enabled.IsSuccess && enabled.Value is { State: RestorationServiceState.Enabled, ConsentGranted: true }
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure(
                enabled.Value?.Detail ?? enabled.ErrorMessage ?? "Owner Mode was not enabled.",
                enabled.ErrorCategory ?? ErrorCategory.ServiceUnavailable);
    }

    internal async Task<OperationResult<bool>> Disable(CancellationToken cancellationToken)
    {
        // Opt-out must work even if the service is missing or recovery cannot run.
        var acquisition = await _leases.AcquireAsync(Timeout, cancellationToken).ConfigureAwait(false);
        if (!acquisition.IsAcquired)
            return OperationResult<bool>.Failure("Owner Mode could not acquire the lease to pause.", ErrorCategory.ServiceUnavailable);
        await using (var lease = acquisition.Lease!)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var off = _consent.SetEnabled(false, lease);
            if (!off.IsSuccess || off.State.Status != MachineConsentStatus.Loaded || off.State.Enabled)
                return OperationResult<bool>.Failure("Owner Mode could not save paused consent: " + off.State.Detail,
                    ErrorCategory.ServiceUnavailable);
        }

        // Release before SCM stop, so an in-flight scan can leave its lease wait.
        var status = _serviceControl.Query(ServiceName);
        if (!status.IsSuccess)
            return status.ErrorCategory == ErrorCategory.NotFound
                ? OperationResult<bool>.Success(true)
                : OperationResult<bool>.Failure(status.ErrorMessage ?? "Service state is unavailable.",
                    status.ErrorCategory ?? ErrorCategory.ServiceUnavailable);

        if (status.Value!.State == ServiceState.Running)
        {
            var stopped = await _serviceControl.StopAsync(ServiceName, Timeout, cancellationToken).ConfigureAwait(false);
            if (!stopped.IsSuccess)
                return stopped;
        }

        return _serviceControl.SetStartType(ServiceName, ServiceStartType.Disabled);
    }
}
