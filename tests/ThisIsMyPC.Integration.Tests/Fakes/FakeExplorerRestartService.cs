using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.Fakes;

public sealed class FakeExplorerRestartService : IExplorerRestartService
{
    public bool WasCalled { get; private set; }
    public bool ShouldSucceed { get; set; } = true;
    public string? FailureMessage { get; set; }

    public bool RefreshWasCalled { get; private set; }

    public Task<OperationResult<bool>> RestartExplorerAsync()
    {
        WasCalled = true;

        var result = ShouldSucceed
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure(
                FailureMessage ?? "Simulated restart failure",
                ErrorCategory.ServiceUnavailable);

        return Task.FromResult(result);
    }

    public Task<OperationResult<bool>> RefreshExplorerViewsAsync()
    {
        RefreshWasCalled = true;
        return Task.FromResult(OperationResult<bool>.Success(true));
    }

    public void Reset()
    {
        WasCalled = false;
        RefreshWasCalled = false;
        ShouldSucceed = true;
        FailureMessage = null;
    }
}
