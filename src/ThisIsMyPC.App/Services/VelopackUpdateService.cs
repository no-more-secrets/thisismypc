using Serilog;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using Velopack;

namespace ThisIsMyPC.App.Services;

public sealed class VelopackUpdateService : IUpdateService, IDisposable
{
    private readonly UpdateManager _manager;
    private readonly IUpdateVerifier? _verifier;
    private readonly ILogger _logger;
    private UpdateInfo? _pendingUpdate;

    public VelopackUpdateService(string updateUrl, IUpdateVerifier? verifier = null, ILogger? logger = null)
    {
        _manager = new UpdateManager(updateUrl);
        _verifier = verifier;
        _logger = logger ?? Log.Logger;
    }

    public async Task<OperationResult<UpdateCheckResult>> CheckForUpdateAsync()
    {
        try
        {
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);

            if (update is null)
            {
                _logger.Debug("No update available");
                return OperationResult<UpdateCheckResult>.Success(
                    new UpdateCheckResult(false, null, null));
            }

            _pendingUpdate = update;
            var version = update.TargetFullRelease.Version.ToString();
            _logger.Information("Update available: {Version}", version);

            return OperationResult<UpdateCheckResult>.Success(
                new UpdateCheckResult(true, version, null));
        }
#pragma warning disable CA1031 // Velopack throws on network/config errors; must not crash
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.Warning(ex, "Update check failed");
            return OperationResult<UpdateCheckResult>.Failure(
                $"Update check failed: {ex.Message}",
                ErrorCategory.ServiceUnavailable,
                ex);
        }
    }

    public async Task<OperationResult<bool>> DownloadUpdateAsync(
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_pendingUpdate is null)
        {
            return OperationResult<bool>.Failure(
                "No pending update to download. Call CheckForUpdateAsync first.",
                ErrorCategory.NotFound);
        }

        try
        {
            Action<int>? progressAction = progress is not null ? v => progress.Report(v) : null;
            await _manager.DownloadUpdatesAsync(_pendingUpdate, progressAction, cancelToken: cancellationToken)
                .ConfigureAwait(false);

            var version = _pendingUpdate.TargetFullRelease.Version.ToString();
            _logger.Information("Update {Version} downloaded", version);

            if (_verifier is not null)
            {
                // Fail-closed: an unresolved package path is a rejection, not a skip.
                _logger.Information("Verifying update integrity for {Version}", version);
                var packagePath = ResolveUpdatePackagePath();
                var verification = await _verifier.VerifyPackageAsync(version, packagePath, cancellationToken)
                    .ConfigureAwait(false);

                if (!verification.IsSuccess)
                {
                    _logger.Error("Update {Version} rejected: {Reason}", version, verification.ErrorMessage);
                    _pendingUpdate = null;
                    return OperationResult<bool>.Failure(
                        "Update could not be verified and has been rejected for security.",
                        ErrorCategory.AccessDenied);
                }

                _logger.Information("Update {Version} integrity verified", version);
            }

            return OperationResult<bool>.Success(true);
        }
#pragma warning disable CA1031 // Velopack throws on download/network errors; must not crash
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.Error(ex, "Update download failed");
            return OperationResult<bool>.Failure(
                $"Update download failed: {ex.Message}",
                ErrorCategory.ServiceUnavailable,
                ex);
        }
    }

    public void ApplyUpdateAndRestart()
    {
        if (_pendingUpdate is null)
        {
            _logger.Warning("ApplyUpdateAndRestart called with no pending update");
            return;
        }

        _logger.Information("Applying update and restarting");
        _manager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
    }

    public void Dispose()
    {
        (_manager as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Resolves the exact file Velopack will apply (_pendingUpdate's target
    /// release) in the staging directory; verifying any other file, such as
    /// newest-by-mtime, would let a stale or delta package answer for the one
    /// being installed. Returns null if absent; the verifier rejects null
    /// (fail-closed).
    /// </summary>
    private string? ResolveUpdatePackagePath()
    {
        try
        {
            var fileName = _pendingUpdate?.TargetFullRelease.FileName;
            if (string.IsNullOrEmpty(fileName))
            {
                _logger.Debug("Pending update has no target release file name");
                return null;
            }

            var path = Path.Combine(AppContext.BaseDirectory, "packages", fileName);
            if (!File.Exists(path))
            {
                _logger.Debug("Downloaded package not found at {Path}", path);
                return null;
            }

            _logger.Debug("Resolved update package path: {Path}", path);
            return path;
        }
#pragma warning disable CA1031 // Path resolution failure must reject, not crash
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.Debug(ex, "Failed to resolve update package path");
            return null;
        }
    }
}
