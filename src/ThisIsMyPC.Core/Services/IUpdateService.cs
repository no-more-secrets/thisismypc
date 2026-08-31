using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

public interface IUpdateService
{
    Task<OperationResult<UpdateCheckResult>> CheckForUpdateAsync();
    Task<OperationResult<bool>> DownloadUpdateAsync(IProgress<int>? progress = null);
    void ApplyUpdateAndRestart();
}

public record UpdateCheckResult(bool IsAvailable, string? Version, string? ReleaseNotes);

/// <summary>
/// Out-of-band update verification (threat model tm2:54). Fail-closed: a package
/// that cannot be positively verified is rejected. There is no fallback path;
/// an unresolved package, missing manifest, bad signature, or digest mismatch
/// all reject the update.
/// </summary>
public interface IUpdateVerifier
{
    /// <param name="updateVersion">The version being verified.</param>
    /// <param name="packageFilePath">Path to the downloaded update package. Null
    /// (path could not be resolved) must be rejected, never skipped.</param>
    Task<OperationResult<bool>> VerifyPackageAsync(
        string updateVersion, string? packageFilePath, CancellationToken cancellationToken = default);
}
