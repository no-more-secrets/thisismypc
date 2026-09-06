namespace ThisIsMyPC.Core.Coordination;

/// <summary>
/// The held-state machine every <see cref="IMutationLease"/> shares, so the
/// Windows lease and test fakes cannot disagree about when a caller may write.
/// Held from construction; the first <see cref="Dispose"/> or
/// <see cref="DisposeAsync"/> runs the release once and every later call is a
/// no-op. Every lease starts with <see cref="RequiresRecovery"/> true and
/// <see cref="CanWrite"/> false; only <see cref="MarkRecovered"/> flips them.
/// <see cref="WasAbandoned"/> is a diagnostic and never affects the gate.
/// </summary>
public abstract class MutationLeaseBase : IMutationLease
{
    private const int Held = 0;
    private const int Released = 1;

    private int _state = Held;
    private volatile bool _requiresRecovery = true;

    protected MutationLeaseBase(string name, string ownerLabel, bool abandoned, DateTimeOffset? acquiredAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerLabel);

        Name = name;
        OwnerLabel = ownerLabel;
        LeaseId = Guid.NewGuid();
        AcquiredAt = acquiredAt ?? DateTimeOffset.UtcNow;
        WasAbandoned = abandoned;
    }

    public Guid LeaseId { get; }

    public string Name { get; }

    public string OwnerLabel { get; }

    public DateTimeOffset AcquiredAt { get; }

    public bool IsHeld => Volatile.Read(ref _state) == Held;

    public bool WasAbandoned { get; }

    public bool RequiresRecovery => _requiresRecovery;

    public bool CanWrite => IsHeld && !_requiresRecovery;

    public void MarkRecovered()
    {
        if (!IsHeld)
            throw new InvalidOperationException("The mutation lease is no longer held.");
        if (!_requiresRecovery)
            throw new InvalidOperationException("The mutation lease was already cleared for writing.");

        _requiresRecovery = false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _state, Released) == Released)
            return;

        ReleaseCore();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _state, Released) == Released)
            return;

        await ReleaseCoreAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the underlying lock. Runs exactly once, on the first disposal.</summary>
    protected abstract void ReleaseCore();

    /// <summary>Asynchronous release; defaults to <see cref="ReleaseCore"/>.</summary>
    protected virtual ValueTask ReleaseCoreAsync()
    {
        ReleaseCore();
        return ValueTask.CompletedTask;
    }
}
