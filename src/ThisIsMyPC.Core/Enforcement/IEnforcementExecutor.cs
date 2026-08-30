using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Enforcement;

/// <summary>
/// Orchestrates multi-step enforced mutations: companion services, scheduled tasks and
/// GPCache entries are handled around the primary mutation, which is supplied as a
/// delegate (the owning module's ApplyChange/RevertChange); the executor never calls
/// modules directly.
/// </summary>
public interface IEnforcementExecutor
{
    Task<EnforcementResult> ExecuteAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyPrimary,
        CancellationToken cancellationToken = default);

    Task<EnforcementResult> RevertAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertPrimary,
        CancellationToken cancellationToken = default);
}
