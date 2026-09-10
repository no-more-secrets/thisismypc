using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.App.UiTests.Fakes;

/// <summary>Full-window diagnostics can resolve the Broker dependency but cannot start privileged work.</summary>
internal sealed class UiDisabledPrivilegeBrokerClient : IPrivilegeBrokerClient
{
    public Task<OperationResult<IPrivilegeBrokerSession>> OpenSessionAsync(BrokerSessionRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<IPrivilegeBrokerSession>.Failure("Privileged operations are disabled in the screenshot harness.", ErrorCategory.AccessDenied));
}
