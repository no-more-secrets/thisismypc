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

    public int DownloadCallCount { get; private set; }
    public int RestartCallCount { get; private set; }
    public OperationResult<bool> DownloadResult { get; set; } = OperationResult<bool>.Success(true);
    public Func<CancellationToken, Task<OperationResult<bool>>>? DownloadHandler { get; set; }

    public Task<OperationResult<bool>> DownloadUpdateAsync(
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        DownloadCallCount++;
        return DownloadHandler?.Invoke(cancellationToken) ?? Task.FromResult(DownloadResult);
    }

    public void ApplyUpdateAndRestart() => RestartCallCount++;
}
