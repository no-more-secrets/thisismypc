using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Tests.Fakes;

public sealed class FakeEnforcementExecutor : IEnforcementExecutor
{
    public EnforcementResult NextExecuteResult { get; set; } = new() { IsSuccess = true };
    public EnforcementResult NextRevertResult { get; set; } = new() { IsSuccess = true };

    /// <summary>
    /// When true, invokes the received primary delegate (like a real executor's
    /// PrimaryMutation step) so tests can verify the mutation routes through it.
    /// </summary>
    public bool InvokePrimary { get; set; }

    public List<ChangeDescriptor> ExecutedChanges { get; } = [];
    public List<ChangeDescriptor> RevertedChanges { get; } = [];
    public Func<ChangeDescriptor, Task<OperationResult<bool>>>? LastApplyDelegate { get; private set; }
    public Func<ChangeDescriptor, Task<OperationResult<bool>>>? LastRevertDelegate { get; private set; }

    public async Task<EnforcementResult> ExecuteAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> applyPrimary,
        CancellationToken cancellationToken = default)
    {
        ExecutedChanges.Add(change);
        LastApplyDelegate = applyPrimary;
        if (InvokePrimary)
        {
            await applyPrimary(change).ConfigureAwait(false);
        }
        return NextExecuteResult;
    }

    public async Task<EnforcementResult> RevertAsync(
        ChangeDescriptor change,
        Func<ChangeDescriptor, Task<OperationResult<bool>>> revertPrimary,
        CancellationToken cancellationToken = default)
    {
        RevertedChanges.Add(change);
        LastRevertDelegate = revertPrimary;
        if (InvokePrimary)
        {
            await revertPrimary(change).ConfigureAwait(false);
        }
        return NextRevertResult;
    }
}
