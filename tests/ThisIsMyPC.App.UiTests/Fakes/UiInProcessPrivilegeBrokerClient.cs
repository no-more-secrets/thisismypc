using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.App.UiTests.Fakes;

/// <summary>
/// Stands in for the elevated broker in full-window sessions. A session runs
/// each change or action through the module registered in the test's own
/// service graph, in this process, with no elevation and no UAC prompt. A
/// change whose module is not registered fails inside the pipeline the way the
/// real broker reports it, so the apply flow can be driven to an unresolved
/// group without writing anything. Owner Mode is refused: it has no in-process
/// equivalent. Tests must still never apply against real modules; see the
/// harness notes in AGENTS.md.
/// </summary>
internal sealed class UiInProcessPrivilegeBrokerClient(
    IEnumerable<IModule> modules,
    IRestorePointService restorePoints) : IPrivilegeBrokerClient
{
    public Task<OperationResult<IPrivilegeBrokerSession>> OpenSessionAsync(BrokerSessionRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<IPrivilegeBrokerSession>.Success(new Session(modules, restorePoints)));

    private sealed class Session(IEnumerable<IModule> modules, IRestorePointService restorePoints) : IPrivilegeBrokerSession
    {
        private IModule? Resolve(string moduleId) => modules.FirstOrDefault(m => m.Info.Name == moduleId);

        public Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change, CancellationToken cancellationToken = default)
            => Resolve(change.ModuleId) is { } module
                ? module.ApplyChangeAsync(change)
                : Task.FromResult(OperationResult<bool>.Failure($"Module '{change.ModuleId}' not found", ErrorCategory.NotFound));

        public Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change, CancellationToken cancellationToken = default)
            => Resolve(change.ModuleId) is { } module
                ? module.RevertChangeAsync(change)
                : Task.FromResult(OperationResult<bool>.Failure($"Module '{change.ModuleId}' not found for revert", ErrorCategory.NotFound));

        public Task<OperationResult<bool>> ExecuteActionAsync(ActionDescriptor action, CancellationToken cancellationToken = default)
            => Resolve(action.ModuleId) is IActionModule module
                ? module.ExecuteActionAsync(action)
                : Task.FromResult(OperationResult<bool>.Failure($"Module '{action.ModuleId}' not found or cannot execute actions", ErrorCategory.NotFound));

        public Task<RestorePointResult> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default)
            => restorePoints.CreateRestorePointAsync(description);

        public Task<OperationResult<bool>> EnableOwnerModeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<bool>.Failure("Owner Mode is disabled in the screenshot harness.", ErrorCategory.AccessDenied));

        public Task<OperationResult<bool>> DisableOwnerModeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<bool>.Failure("Owner Mode is disabled in the screenshot harness.", ErrorCategory.AccessDenied));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
