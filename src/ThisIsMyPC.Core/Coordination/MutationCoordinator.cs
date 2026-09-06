using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Coordination;

/// <summary>Coordination status is separate from the operation's own result.</summary>
public sealed class MutationRunResult<T>
{
    internal MutationRunResult(MutationLeaseResult acquisition, OperationResult<bool>? recovery,
        bool operationRan, T? value)
    {
        Acquisition = acquisition;
        Recovery = recovery;
        OperationRan = operationRan;
        Value = value;
    }

    /// <summary>Original provider diagnostics. Any acquired lease is already released on return.</summary>
    public MutationLeaseResult Acquisition { get; }
    public OperationResult<bool>? Recovery { get; }
    public bool OperationRan { get; }
    public T? Value { get; }
}

/// <summary>
/// Acquires once, recovers every acquisition, and holds through the complete operation.
/// Callbacks are trusted: neither may dispose, retain, or independently clear the lease.
/// </summary>
public sealed class MutationCoordinator
{
    private static readonly AsyncLocal<ActiveScope?> Active = new();
    private readonly IMutationLeaseProvider _provider;
    private readonly Func<IMutationLease, CancellationToken, Task<OperationResult<bool>>> _recover;

    public MutationCoordinator(IMutationLeaseProvider provider,
        Func<IMutationLease, CancellationToken, Task<OperationResult<bool>>> recover)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(recover);
        _provider = provider;
        _recover = recover;
    }

    public async Task<MutationRunResult<T>> RunAsync<T>(TimeSpan maxWait,
        Func<IMutationLease, CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWait, TimeSpan.Zero);
        var previous = Active.Value;
        for (var scope = previous; scope is not null; scope = scope.Parent)
            if (string.Equals(scope.Name, _provider.Name, StringComparison.Ordinal))
                throw new InvalidOperationException("Nested mutation coordination for the same lease is not allowed.");
        Active.Value = new ActiveScope(_provider.Name, previous);
        try
        {
            var acquisition = await _provider.AcquireAsync(maxWait, cancellationToken).ConfigureAwait(false);
            if (!acquisition.IsAcquired)
                return new(acquisition, null, false, default);
            var lease = acquisition.Lease!;
            MutationRunResult<T> result;
            try
            {
                result = await RunHeldAsync(acquisition, operation, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                try { await lease.DisposeAsync().ConfigureAwait(false); }
                catch (Exception releaseFailure)
                {
                    throw new AggregateException("Operation and lease release both failed.", failure, releaseFailure);
                }
                throw;
            }
            await lease.DisposeAsync().ConfigureAwait(false);
            return result;
        }
        finally { Active.Value = previous; }
    }

    private async Task<MutationRunResult<T>> RunHeldAsync<T>(MutationLeaseResult acquisition,
        Func<IMutationLease, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var lease = acquisition.Lease!;
        cancellationToken.ThrowIfCancellationRequested();
        if (!lease.IsHeld || !lease.RequiresRecovery || lease.CanWrite ||
            !string.Equals(lease.Name, _provider.Name, StringComparison.Ordinal))
            throw new InvalidOperationException("Provider must return a matching unrecovered lease.");
        var recovery = await _recover(lease, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!recovery.IsSuccess || !recovery.Value)
            return new(acquisition, recovery, false, default);
        // Only the coordinator clears recovery, after explicit affirmative completion.
        lease.MarkRecovered();
        if (!lease.CanWrite) throw new InvalidOperationException("Recovery did not clear the held lease.");
        var value = await operation(lease, cancellationToken).ConfigureAwait(false);
        return new(acquisition, recovery, true, value);
    }

    private sealed record ActiveScope(string Name, ActiveScope? Parent);
}
