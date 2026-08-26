using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

/// <summary>
/// Windows service control via SCM. The app runs elevated, so AccessDenied failures
/// indicate service protection (LIGHT-protected services like WinDefend), never missing elevation.
/// All failures surface as OperationResult; the one exception is caller-requested cancellation,
/// which propagates as OperationCanceledException from the async members.
/// </summary>
public interface IServiceControlService
{
    /// <summary>Current state, start type, and display name — the before-state for a ChangeDescriptor.</summary>
    OperationResult<ServiceStatusInfo> Query(string serviceName);

    OperationResult<bool> SetStartType(string serviceName, ServiceStartType startType);

    /// <summary>Stops the service and waits until it reports Stopped or <paramref name="timeout"/> elapses.</summary>
    Task<OperationResult<bool>> StopAsync(string serviceName, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Starts the service and waits until it reports Running or <paramref name="timeout"/> elapses.</summary>
    Task<OperationResult<bool>> StartAsync(string serviceName, TimeSpan timeout, CancellationToken cancellationToken = default);
}
