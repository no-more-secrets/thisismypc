namespace ThisIsMyPC.Core.Coordination;

/// <summary>
/// One held machine-wide mutation lease. Exactly one process on the PC holds it
/// at a time: the app and the Session 0 service take the same lease around every
/// consent change, baseline write, journal write, apply, undo, redo, history
/// import, and journal rotation. Disposing releases it.
///
/// Every lease starts unable to write. The holder must run recovery (read the
/// journal for intents with no outcome) and then call <see cref="MarkRecovered"/>
/// before <see cref="CanWrite"/> turns true. That is required whether or not
/// the kernel reported the previous holder as abandoned, because abandonment is
/// not a reliable signal: a lease released without recovery consumes the
/// abandoned flag, and a sole holder that crashes destroys the object, so the
/// next acquisition creates a fresh, clean-looking mutex. <see cref="WasAbandoned"/>
/// is kept as a diagnostic only.
/// </summary>
public interface IMutationLease : IDisposable, IAsyncDisposable
{
    /// <summary>Unique per acquisition; the journal records it with each intent.</summary>
    Guid LeaseId { get; }

    /// <summary>The name the lease was taken under (the lock object name).</summary>
    string Name { get; }

    /// <summary>Who took the lease, for diagnostics ("app" or "service").</summary>
    string OwnerLabel { get; }

    DateTimeOffset AcquiredAt { get; }

    /// <summary>True from acquisition until disposal.</summary>
    bool IsHeld { get; }

    /// <summary>
    /// Diagnostic: the kernel reported that the previous holder ended without
    /// releasing. Does not change what the holder must do; recovery is always
    /// required. False does not mean the previous holder finished cleanly.
    /// </summary>
    bool WasAbandoned { get; }

    /// <summary>
    /// True from acquisition until <see cref="MarkRecovered"/>. While true, the
    /// holder must not write.
    /// </summary>
    bool RequiresRecovery { get; }

    /// <summary>
    /// True only while held and after <see cref="MarkRecovered"/>. Every writer
    /// checks this immediately before its first write and treats false as "do
    /// not write".
    /// </summary>
    bool CanWrite { get; }

    /// <summary>
    /// Declares that recovery ran for this acquisition. Throws
    /// <see cref="InvalidOperationException"/> when the lease is not held or was
    /// already cleared.
    /// </summary>
    void MarkRecovered();
}
