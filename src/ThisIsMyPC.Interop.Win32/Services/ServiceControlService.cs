using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using static ThisIsMyPC.Interop.Win32.Services.NativeServiceControl;

namespace ThisIsMyPC.Interop.Win32.Services;

public sealed class ServiceControlService : IServiceControlService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public OperationResult<ServiceStatusInfo> Query(string serviceName)
    {
        try
        {
            return WithService(serviceName, SERVICE_QUERY_STATUS | SERVICE_QUERY_CONFIG, hService =>
            {
                var statusResult = QueryState(hService, serviceName);
                if (!statusResult.IsSuccess)
                    return OperationResult<ServiceStatusInfo>.Failure(
                        statusResult.ErrorMessage!, statusResult.ErrorCategory!.Value, statusResult.Exception);

                var configResult = QueryConfig(hService, serviceName);
                if (!configResult.IsSuccess)
                    return OperationResult<ServiceStatusInfo>.Failure(
                        configResult.ErrorMessage!, configResult.ErrorCategory!.Value, configResult.Exception);

                var (startType, displayName) = configResult.Value;
                return OperationResult<ServiceStatusInfo>.Success(
                    new ServiceStatusInfo(serviceName, displayName, statusResult.Value, startType));
            });
        }
        catch (Exception ex)
        {
            return OperationResult<ServiceStatusInfo>.Failure(
                $"Unexpected error querying service '{serviceName}': {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public OperationResult<bool> SetStartType(string serviceName, ServiceStartType startType)
    {
        try
        {
            return WithService(serviceName, SERVICE_CHANGE_CONFIG, hService =>
            {
                var scmStartType = startType switch
                {
                    ServiceStartType.Automatic or ServiceStartType.AutomaticDelayed => SERVICE_AUTO_START,
                    ServiceStartType.Manual => SERVICE_DEMAND_START,
                    ServiceStartType.Disabled => SERVICE_DISABLED,
                    _ => SERVICE_DEMAND_START,
                };

                if (!ChangeServiceConfigW(hService, SERVICE_NO_CHANGE, scmStartType, SERVICE_NO_CHANGE,
                        null, null, 0, null, null, null, null))
                    return MapLastError<bool>(serviceName, "change start type of");

                // Delayed flag lives outside dwStartType; set it explicitly both ways so
                // Automatic clears a previously-delayed configuration. If this second call
                // fails the start type has already changed — the caller's rollback restores
                // the full before-state, so no local undo is attempted.
                var delayed = startType == ServiceStartType.AutomaticDelayed ? 1 : 0;
                if (startType is ServiceStartType.Automatic or ServiceStartType.AutomaticDelayed
                    && !ChangeServiceConfig2W(hService, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref delayed))
                    return MapLastError<bool>(serviceName, "set delayed-autostart flag on");

                return OperationResult<bool>.Success(true);
            });
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure(
                $"Unexpected error configuring service '{serviceName}': {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public async Task<OperationResult<bool>> StopAsync(
        string serviceName, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            var initiate = WithService(serviceName, SERVICE_STOP | SERVICE_QUERY_STATUS, hService =>
            {
                if (!ControlService(hService, SERVICE_CONTROL_STOP, out _))
                {
                    var error = Marshal.GetLastWin32Error();
                    // NOT_ACTIVE: already stopped. CANNOT_ACCEPT_CTRL: already STOP_PENDING
                    // (services clear dwControlsAccepted while stopping) — fall through to the wait.
                    if (error is ERROR_SERVICE_NOT_ACTIVE or ERROR_SERVICE_CANNOT_ACCEPT_CTRL)
                        return OperationResult<bool>.Success(true);
                    return MapError<bool>(error, serviceName, "stop");
                }
                return OperationResult<bool>.Success(true);
            });

            if (!initiate.IsSuccess)
                return initiate;

            return await WaitForStateAsync(serviceName, ServiceState.Stopped, timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure(
                $"Unexpected error stopping service '{serviceName}': {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public async Task<OperationResult<bool>> StartAsync(
        string serviceName, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            var initiate = WithService(serviceName, SERVICE_START | SERVICE_QUERY_STATUS, hService =>
            {
                if (!StartServiceW(hService, 0, 0))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ERROR_SERVICE_ALREADY_RUNNING)
                        return OperationResult<bool>.Success(true);
                    return MapError<bool>(error, serviceName, "start");
                }
                return OperationResult<bool>.Success(true);
            });

            if (!initiate.IsSuccess)
                return initiate;

            return await WaitForStateAsync(serviceName, ServiceState.Running, timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure(
                $"Unexpected error starting service '{serviceName}': {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    private async Task<OperationResult<bool>> WaitForStateAsync(
        string serviceName, ServiceState targetState, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // Negative timeout (incl. Timeout.InfiniteTimeSpan) means wait indefinitely.
        var deadline = timeout < TimeSpan.Zero
            ? long.MaxValue
            : Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var poll = WithService(serviceName, SERVICE_QUERY_STATUS, h => QueryState(h, serviceName));
            if (!poll.IsSuccess)
                return OperationResult<bool>.Failure(poll.ErrorMessage!, poll.ErrorCategory!.Value, poll.Exception);
            if (poll.Value == targetState)
                return OperationResult<bool>.Success(true);

            if (Environment.TickCount64 >= deadline)
                return OperationResult<bool>.Failure(
                    $"Service '{serviceName}' did not reach state {targetState} within {timeout.TotalSeconds:0.#}s (currently {poll.Value}).",
                    ErrorCategory.ServiceUnavailable);

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static OperationResult<T> WithService<T>(
        string serviceName, uint desiredAccess, Func<nint, OperationResult<T>> action)
    {
        var hScm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (hScm == 0)
            return MapLastError<T>(serviceName, "connect to Service Control Manager for");
        try
        {
            var hService = OpenServiceW(hScm, serviceName, desiredAccess);
            if (hService == 0)
                return MapLastError<T>(serviceName, "open");
            try
            {
                return action(hService);
            }
            finally
            {
                CloseServiceHandle(hService);
            }
        }
        finally
        {
            CloseServiceHandle(hScm);
        }
    }

    private static OperationResult<ServiceState> QueryState(nint hService, string serviceName)
    {
        var size = (uint)Marshal.SizeOf<ServiceStatusProcess>();
        if (!QueryServiceStatusEx(hService, SC_STATUS_PROCESS_INFO, out var status, size, out _))
            return MapLastError<ServiceState>(serviceName, "query status of");

        var state = status.dwCurrentState switch
        {
            SERVICE_STOPPED => ServiceState.Stopped,
            SERVICE_START_PENDING => ServiceState.StartPending,
            SERVICE_STOP_PENDING => ServiceState.StopPending,
            SERVICE_RUNNING => ServiceState.Running,
            SERVICE_CONTINUE_PENDING => ServiceState.ContinuePending,
            SERVICE_PAUSE_PENDING => ServiceState.PausePending,
            SERVICE_PAUSED => ServiceState.Paused,
            _ => ServiceState.Stopped,
        };
        return OperationResult<ServiceState>.Success(state);
    }

    private static OperationResult<(ServiceStartType StartType, string DisplayName)> QueryConfig(
        nint hService, string serviceName)
    {
        QueryServiceConfigW(hService, 0, 0, out var needed);
        if (Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER || needed == 0)
            return MapLastError<(ServiceStartType, string)>(serviceName, "query configuration of");

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!QueryServiceConfigW(hService, buffer, needed, out _))
                return MapLastError<(ServiceStartType, string)>(serviceName, "query configuration of");

            var config = Marshal.PtrToStructure<QueryServiceConfig>(buffer);
            var displayName = config.lpDisplayName != 0
                ? Marshal.PtrToStringUni(config.lpDisplayName) ?? serviceName
                : serviceName;

            ServiceStartType startType;
            switch (config.dwStartType)
            {
                case SERVICE_AUTO_START:
                    // A wrong delayed flag would corrupt the captured before-state,
                    // so a failed read is a query failure, not "not delayed".
                    if (!QueryServiceConfig2W(hService, SERVICE_CONFIG_DELAYED_AUTO_START_INFO,
                            out var delayed, sizeof(int), out _))
                        return MapLastError<(ServiceStartType, string)>(serviceName, "query delayed-autostart flag of");
                    startType = delayed != 0 ? ServiceStartType.AutomaticDelayed : ServiceStartType.Automatic;
                    break;
                case SERVICE_DISABLED:
                    startType = ServiceStartType.Disabled;
                    break;
                default:
                    startType = ServiceStartType.Manual; // demand-start; boot/system driver start types also fold here
                    break;
            }

            return OperationResult<(ServiceStartType, string)>.Success((startType, displayName));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static OperationResult<T> MapLastError<T>(string serviceName, string verb)
        => MapError<T>(Marshal.GetLastWin32Error(), serviceName, verb);

    private static OperationResult<T> MapError<T>(int win32Error, string serviceName, string verb)
    {
        // The app always runs elevated: access-denied means the service is protected
        // (e.g. LIGHT-protected Defender services), never missing elevation.
        (string message, ErrorCategory category) = win32Error switch
        {
            ERROR_ACCESS_DENIED => (
                $"Cannot {verb} service '{serviceName}': the service is protected by Windows (process protection or SCM lockdown).",
                ErrorCategory.AccessDenied),
            ERROR_SERVICE_DOES_NOT_EXIST => (
                $"Cannot {verb} service '{serviceName}': no service with that name exists.",
                ErrorCategory.NotFound),
            ERROR_SERVICE_CANNOT_ACCEPT_CTRL => (
                $"Cannot {verb} service '{serviceName}': the service cannot accept control requests right now.",
                ErrorCategory.ServiceUnavailable),
            ERROR_SERVICE_NOT_ACTIVE => (
                $"Cannot {verb} service '{serviceName}': the service is not running.",
                ErrorCategory.ServiceUnavailable),
            _ => (
                $"Cannot {verb} service '{serviceName}': Win32 error {win32Error}.",
                ErrorCategory.ServiceUnavailable),
        };
        return OperationResult<T>.Failure(message, category);
    }
}
