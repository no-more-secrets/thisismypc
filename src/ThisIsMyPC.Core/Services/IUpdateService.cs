using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

public interface IUpdateService
{
    Task<OperationResult<UpdateCheckResult>> CheckForUpdateAsync();
    Task<OperationResult<bool>> DownloadUpdateAsync(IProgress<int>? progress = null);
    void ApplyUpdateAndRestart();
}

public record UpdateCheckResult(bool IsAvailable, string? Version, string? ReleaseNotes);

public interface IUpdateVerifier
{
    /// <param name="updateVersion">The version being verified.</param>
    /// <param name="packageFilePath">Path to the downloaded update binary, if resolvable.
    /// When null, the verifier should fall back to verifying the current application binary
    /// (build pipeline integrity check).</param>
    OperationResult<bool> VerifyPackageIntegrity(string updateVersion, string? packageFilePath = null);
}
