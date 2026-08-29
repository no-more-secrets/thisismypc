using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.Fakes;

internal sealed class FakeUpdateService : IUpdateService
{
    public int CheckCallCount { get; private set; }
    public OperationResult<UpdateCheckResult> NextResult { get; set; } =
        OperationResult<UpdateCheckResult>.Success(new UpdateCheckResult(false, null, null));

    public Task<OperationResult<UpdateCheckResult>> CheckForUpdateAsync()
    {
        CheckCallCount++;
        return Task.FromResult(NextResult);
    }

    public Task<OperationResult<bool>> DownloadUpdateAsync(IProgress<int>? progress = null) =>
        Task.FromResult(OperationResult<bool>.Success(true));

    public void ApplyUpdateAndRestart()
    {
    }
}
