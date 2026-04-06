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
#pragma warning disable CA1031 // Velopack throws on network/config errors — must not crash
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

    public async Task<OperationResult<bool>> DownloadUpdateAsync(IProgress<int>? progress = null)
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
            await _manager.DownloadUpdatesAsync(_pendingUpdate, progressAction).ConfigureAwait(false);

            var version = _pendingUpdate.TargetFullRelease.Version.ToString();
            _logger.Information("Update {Version} downloaded", version);

            if (_verifier is not null)
            {
                _logger.Information("Verifying update integrity for {Version}", version);
                var packagePath = ResolveUpdatePackagePath();
                var verification = _verifier.VerifyPackageIntegrity(version, packagePath);

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
#pragma warning disable CA1031 // Velopack throws on download/network errors — must not crash
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
    /// Attempts to find the downloaded update package in Velopack's staging directory.
    /// Returns null if the path cannot be resolved (verifier falls back to current binary).
    /// </summary>
    private string? ResolveUpdatePackagePath()
    {
        try
        {
            var packagesDir = Path.Combine(AppContext.BaseDirectory, "packages");
            if (!Directory.Exists(packagesDir))
            {
                _logger.Debug("Velopack packages directory not found at {Path}", packagesDir);
                return null;
            }

            // Find the most recently written .nupkg file in the packages directory
            var newest = Directory.GetFiles(packagesDir, "*.nupkg")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (newest is null)
            {
                _logger.Debug("No .nupkg files found in {Path}", packagesDir);
                return null;
            }

            _logger.Debug("Resolved update package path: {Path}", newest.FullName);
            return newest.FullName;
        }
#pragma warning disable CA1031 // Path resolution failure should not block updates
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.Debug(ex, "Failed to resolve update package path");
            return null;
        }
    }
}
