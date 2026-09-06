using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Keeps Core's enforcement routing intact in the unelevated UI. The supplied
/// delegate sends the complete descriptor to the broker, which performs enforcement.
/// </summary>
public sealed class BrokerRoutingEnforcementExecutor : IEnforcementExecutor
{
    public Task<EnforcementResult> ExecuteAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyPrimary,
        CancellationToken cancellationToken = default) =>
        Route(change, applyPrimary, cancellationToken);

    public Task<EnforcementResult> RevertAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertPrimary,
        CancellationToken cancellationToken = default) =>
        Route(change, revertPrimary, cancellationToken);

    private static async Task<EnforcementResult> Route(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await operation(change).ConfigureAwait(false);
        return result.IsSuccess
            ? new EnforcementResult { IsSuccess = true }
            : new EnforcementResult
            {
                IsSuccess = false,
                ErrorMessage = result.ErrorMessage,
                ErrorCategory = result.ErrorCategory ?? ErrorCategory.ServiceUnavailable,
            };
    }
}
