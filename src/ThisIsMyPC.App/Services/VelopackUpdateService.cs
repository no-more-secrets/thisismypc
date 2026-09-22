using NLog;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using Velopack;

namespace ThisIsMyPC.App.Services;

public sealed class VelopackUpdateService : IUpdateService, IDisposable
{
    private readonly UpdateManager _manager;
    private readonly ILogger _logger;

    public VelopackUpdateService(string updateUrl, ILogger? logger = null)
        : this(new UpdateManager(updateUrl), logger) { }

    internal VelopackUpdateService(UpdateManager manager, ILogger? logger = null)
    {
        _manager = manager;
        _logger = logger ?? LogManager.GetLogger("ThisIsMyPC.App.Services.VelopackUpdateService");
    }

    public async Task<OperationResult<UpdateCheckResult>> CheckForUpdateAsync()
    {
        // Development and other unpackaged runs have no Velopack installation to update.
        if (!_manager.IsInstalled)
            return OperationResult<UpdateCheckResult>.Success(new UpdateCheckResult(false, null, null));
        try
        {
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);

            if (update is null)
            {
                _logger.Debug("No update available");
                return OperationResult<UpdateCheckResult>.Success(
                    new UpdateCheckResult(false, null, null));
            }

            var version = update.TargetFullRelease.Version.ToString();
            _logger.Info("Update available: {Version}", version);

            return OperationResult<UpdateCheckResult>.Success(
                new UpdateCheckResult(true, version, null));
        }
#pragma warning disable CA1031 // Velopack throws on network/config errors; must not crash
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.Warn(ex, "Update check failed");
            return OperationResult<UpdateCheckResult>.Failure(
                $"Update check failed: {ex.Message}",
                ErrorCategory.ServiceUnavailable,
                ex);
        }
    }

    public bool SupportsInAppUpdate => false;

    public Task<OperationResult<bool>> DownloadUpdateAsync(
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<bool>.Failure(
            "Install this update from the signed installer on the releases page.",
            ErrorCategory.AccessDenied));
    }

    public void ApplyUpdateAndRestart() =>
        _logger.Warn("In-app update apply is disabled; use the signed release installer");

    public void Dispose()
    {
        (_manager as IDisposable)?.Dispose();
    }
}
