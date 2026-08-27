using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Modules.Annoyances.Tests.Fakes;

/// <summary>
/// Executes the primary mutation delegate and nothing else — the production executor's
/// behavior for informational-only enforcement (ReversionVectors without companions),
/// which is all this module's drift-fragile settings carry.
/// </summary>
public sealed class PassthroughEnforcementExecutor : IEnforcementExecutor
{
    public async Task<EnforcementResult> ExecuteAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyPrimary,
        CancellationToken cancellationToken = default)
        => ToEnforcementResult(await applyPrimary(change).ConfigureAwait(false));

    public async Task<EnforcementResult> RevertAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertPrimary,
        CancellationToken cancellationToken = default)
        => ToEnforcementResult(await revertPrimary(change).ConfigureAwait(false));

    private static EnforcementResult ToEnforcementResult(OperationResult<bool> result) => new()
    {
        IsSuccess = result.IsSuccess,
        ErrorMessage = result.ErrorMessage,
        ErrorCategory = result.ErrorCategory,
    };
}
