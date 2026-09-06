namespace ThisIsMyPC.Core.Coordination;

/// <summary>
/// How one <see cref="IMutationLeaseProvider.AcquireAsync"/> call ended. Only
/// <see cref="Acquired"/> and <see cref="AcquiredAbandoned"/> carry a lease.
/// </summary>
public enum MutationLeaseOutcome
{
    /// <summary>
    /// The lease is held and the kernel did not report abandonment. This is not
    /// proof the previous holder finished: a sole holder that crashed destroys
    /// the object and the next acquisition creates a fresh one. Recovery is
    /// still required before <see cref="IMutationLease.CanWrite"/> turns true.
    /// </summary>
    Acquired,

    /// <summary>
    /// The lease is held and the kernel reported that the previous holder ended
    /// without releasing (its thread or process died while holding, and another
    /// handle kept the object alive). Diagnostic only; the recovery requirement
    /// is the same as for <see cref="Acquired"/>.
    /// </summary>
    AcquiredAbandoned,

    /// <summary>The bounded wait elapsed while another holder kept the lease.</summary>
    TimedOut,

    /// <summary>The caller's token was cancelled before the lease was granted.</summary>
    Cancelled,

    /// <summary>
    /// The lock object could not be trusted or created with the required
    /// protection: the name already existed with a different DACL or owner, the
    /// security descriptor could not be built, or access was denied. Nothing was
    /// weakened to get in; the caller must not write.
    /// </summary>
    Refused,

    /// <summary>An operating system call failed for a reason other than trust.</summary>
    Faulted,
}
