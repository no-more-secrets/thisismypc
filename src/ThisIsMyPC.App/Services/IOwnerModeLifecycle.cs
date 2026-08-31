using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// The slice of the Owner Mode service lifecycle that cards need for the
/// actionable degradation callout: turn the service on, and hear about
/// lifecycle transitions so degraded state can refresh live.
/// </summary>
public interface IOwnerModeLifecycle
{
    /// <summary>Raised after an enable/disable completes so capability-dependent UI can refresh.</summary>
    event EventHandler? StateChanged;

    Task<OperationResult<bool>> EnableAsync(CancellationToken cancellationToken = default);
}
