namespace ThisIsMyPC.Core.Coordination;

/// <summary>
/// Grants the machine-wide mutation lease (<see cref="IMutationLease"/>).
/// Acquisition is awaitable with a bounded wait and a cancellation token; it
/// never blocks the caller's thread on the lock itself. Implementations own the
/// thread affinity of the underlying OS lock: the caller may hold the returned
/// lease across any async continuation and dispose it from any thread.
/// </summary>
public interface IMutationLeaseProvider
{
    /// <summary>The lock object name this provider acquires.</summary>
    string Name { get; }

    /// <summary>
    /// Waits up to <paramref name="maxWait"/> for the lease. The wait must be
    /// finite and non-negative; zero probes without waiting. Returns rather than
    /// throws for every outcome except argument errors. A result whose
    /// <see cref="MutationLeaseResult.Lease"/> is non-null must be disposed.
    /// </summary>
    Task<MutationLeaseResult> AcquireAsync(TimeSpan maxWait, CancellationToken cancellationToken = default);
}
