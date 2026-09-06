using ThisIsMyPC.Core.Results;
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

    private readonly ServiceInstaller _installer = new();
    private readonly ServiceControlService _serviceControl = new();
    private readonly IIpcClient _ipc = new IpcClient();

    internal async Task<OperationResult<bool>> Enable(CancellationToken cancellationToken)
    {
        var binaryPath = Path.Combine(AppContext.BaseDirectory, "ThisIsMyPC.Service.exe");
        if (!File.Exists(binaryPath))
            return OperationResult<bool>.Failure("The Owner Mode service binary is missing.", ErrorCategory.NotFound);

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
        var status = _serviceControl.Query(ServiceName);
        if (!status.IsSuccess && status.ErrorCategory == ErrorCategory.NotFound)
            return OperationResult<bool>.Success(true);

        if (status.IsSuccess && status.Value!.State == ServiceState.Running)
        {
            var paused = await _ipc.PauseRestorationAsync(cancellationToken).ConfigureAwait(false);
            if (!paused.IsSuccess || paused.Value is not { State: RestorationServiceState.Paused, ConsentGranted: false })
            {
                return OperationResult<bool>.Failure(
                    paused.Value?.Detail ?? paused.ErrorMessage ?? "Owner Mode pause was not confirmed.",
                    ErrorCategory.ServiceUnavailable);
            }
            var stopped = await _serviceControl.StopAsync(ServiceName, Timeout, cancellationToken).ConfigureAwait(false);
            if (!stopped.IsSuccess)
                return stopped;
        }

        return _serviceControl.SetStartType(ServiceName, ServiceStartType.Disabled);
    }
}
