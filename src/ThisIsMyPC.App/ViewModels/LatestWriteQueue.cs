using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Latest-wins write coalescing for live hardware controls: a moving slider
/// posts values faster than a device accepts them; one write runs at a time
/// and only the newest posted one survives. <see cref="RunAsync"/> is for
/// writes that must not be dropped (a save): it waits for the posted writes
/// to drain, then runs alone.
/// </summary>
public sealed class LatestWriteQueue
{
    private readonly Action<string?> _report;
    private readonly SemaphoreSlim _exclusive = new(1, 1);
    private Func<Task<OperationResult<bool>>>? _pending;
    private bool _draining;

    public LatestWriteQueue(Action<string?> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _report = report;
    }

    /// <summary>Queues a write; an earlier queued write that has not started is replaced.</summary>
    public void Post(Func<Task<OperationResult<bool>>> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        _pending = write;
        if (!_draining)
            _ = DrainAsync();
    }

    /// <summary>Runs one write after everything posted so far, never replaced, and returns its result.</summary>
    public async Task<OperationResult<bool>> RunAsync(Func<Task<OperationResult<bool>>> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        await _exclusive.WaitAsync().ConfigureAwait(true);
        try
        {
            var result = await write().ConfigureAwait(true);
            _report(result.IsSuccess ? null : result.ErrorMessage);
            return result;
        }
        finally
        {
            _exclusive.Release();
        }
    }

    private async Task DrainAsync()
    {
        _draining = true;
        try
        {
            await _exclusive.WaitAsync().ConfigureAwait(true);
            try
            {
                while (_pending is { } next)
                {
                    _pending = null;
                    var result = await next().ConfigureAwait(true);
                    _report(result.IsSuccess ? null : result.ErrorMessage);
                }
            }
            finally
            {
                _exclusive.Release();
            }
        }
        finally
        {
            _draining = false;
        }
    }
}
