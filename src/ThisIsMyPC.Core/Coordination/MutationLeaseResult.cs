namespace ThisIsMyPC.Core.Coordination;

/// <summary>
/// Outcome of one acquisition. <see cref="Lease"/> is non-null exactly when
/// <see cref="Outcome"/> is <see cref="MutationLeaseOutcome.Acquired"/> or
/// <see cref="MutationLeaseOutcome.AcquiredAbandoned"/>; the factories refuse
/// any other pairing so a caller can never hold a lease it was told it lacks,
/// or be told it holds one it does not.
/// </summary>
public sealed class MutationLeaseResult
{
    private MutationLeaseResult(
        MutationLeaseOutcome outcome,
        IMutationLease? lease,
        string? errorMessage,
        int? nativeErrorCode,
        Exception? exception)
    {
        Outcome = outcome;
        Lease = lease;
        ErrorMessage = errorMessage;
        NativeErrorCode = nativeErrorCode;
        Exception = exception;
    }

    public MutationLeaseOutcome Outcome { get; }

    /// <summary>The held lease; null unless <see cref="IsAcquired"/>.</summary>
    public IMutationLease? Lease { get; }

    public string? ErrorMessage { get; }

    /// <summary>Win32 error code when the OS reported one; diagnostics only.</summary>
    public int? NativeErrorCode { get; }

    public Exception? Exception { get; }

    /// <summary>True for Acquired and AcquiredAbandoned.</summary>
    public bool IsAcquired => Lease is not null;

    /// <summary>
    /// True only after the holder ran recovery and called
    /// <see cref="IMutationLease.MarkRecovered"/>. Every acquisition, clean or
    /// abandoned, reads false here first.
    /// </summary>
    public bool CanWrite => Lease?.CanWrite == true;

    /// <summary>
    /// Wraps a held lease. The outcome is read from the lease itself so the two
    /// cannot disagree: a lease the kernel reported abandoned reads
    /// AcquiredAbandoned. Both outcomes require recovery before writing.
    /// </summary>
    public static MutationLeaseResult Held(IMutationLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!lease.IsHeld)
            throw new ArgumentException("A held result needs a lease that is held.", nameof(lease));

        var outcome = lease.WasAbandoned
            ? MutationLeaseOutcome.AcquiredAbandoned
            : MutationLeaseOutcome.Acquired;
        return new MutationLeaseResult(outcome, lease, null, null, null);
    }

    public static MutationLeaseResult TimedOut(TimeSpan waited)
        => new(MutationLeaseOutcome.TimedOut, null,
            $"Mutation lease not granted within {waited.TotalMilliseconds:0} ms", null, null);

    public static MutationLeaseResult Cancelled()
        => new(MutationLeaseOutcome.Cancelled, null, "Mutation lease wait cancelled", null, null);

    public static MutationLeaseResult Refused(string message, int? nativeErrorCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new MutationLeaseResult(MutationLeaseOutcome.Refused, null, message, nativeErrorCode, null);
    }

    public static MutationLeaseResult Faulted(string message, int? nativeErrorCode = null, Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new MutationLeaseResult(MutationLeaseOutcome.Faulted, null, message, nativeErrorCode, exception);
    }
}
