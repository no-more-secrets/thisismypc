using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.UiTests.Fakes;

/// <summary>No Velopack bootstrap in the test host; reports "up to date".</summary>
public sealed class UiFakeUpdateService : IUpdateService
{
    public Task<OperationResult<UpdateCheckResult>> CheckForUpdateAsync() =>
        Task.FromResult(OperationResult<UpdateCheckResult>.Success(
            new UpdateCheckResult(IsAvailable: false, Version: null, ReleaseNotes: null)));

    public Task<OperationResult<bool>> DownloadUpdateAsync(
        IProgress<int>? progress = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult<bool>.Success(true));

    public void ApplyUpdateAndRestart()
    {
    }
}
