using ThisIsMyPC.Core.Coordination;

namespace ThisIsMyPC.Core.Tests.Coordination;

/// <summary>
/// In-memory lease with the production state machine (<see cref="MutationLeaseBase"/>)
/// and a counted release, so the contract can be exercised without a kernel object.
/// </summary>
public sealed class FakeMutationLease : MutationLeaseBase
{
    private int _releaseCount;

    public FakeMutationLease(string name, string ownerLabel, bool abandoned)
        : base(name, ownerLabel, abandoned)
    {
    }

    public int ReleaseCount => Volatile.Read(ref _releaseCount);

    public Action? OnRelease { get; set; }

    protected override void ReleaseCore()
    {
        Interlocked.Increment(ref _releaseCount);
        OnRelease?.Invoke();
    }
}

/// <summary>
/// A provider that grants one in-memory lease at a time. While a granted lease
/// is held, further acquisitions time out (mirroring the kernel mutex), unless a
/// scripted result is queued, in which case the script wins.
/// </summary>
public sealed class FakeMutationLeaseProvider : IMutationLeaseProvider
{
    private readonly Queue<Func<MutationLeaseResult>> _scripted = new();
    private readonly List<(TimeSpan MaxWait, bool CancellationRequested)> _requests = [];
    private FakeMutationLease? _current;

    public FakeMutationLeaseProvider(string? name = null, string ownerLabel = "test")
    {
        Name = name ?? MutationLeaseNames.ForTest();
        OwnerLabel = ownerLabel;
    }

    public string Name { get; }

    public string OwnerLabel { get; }

    /// <summary>True when the previous holder is to be reported as abandoned on the next grant.</summary>
    public bool NextGrantIsAbandoned { get; set; }

    public IReadOnlyList<(TimeSpan MaxWait, bool CancellationRequested)> Requests => _requests;

    public FakeMutationLease? CurrentLease => _current is { IsHeld: true } ? _current : null;

    public void Script(Func<MutationLeaseResult> result) => _scripted.Enqueue(result);

    public Task<MutationLeaseResult> AcquireAsync(TimeSpan maxWait, CancellationToken cancellationToken = default)
    {
        if (maxWait < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxWait));

        _requests.Add((maxWait, cancellationToken.IsCancellationRequested));

        if (_scripted.Count > 0)
            return Task.FromResult(_scripted.Dequeue()());

        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(MutationLeaseResult.Cancelled());

        if (_current is { IsHeld: true })
            return Task.FromResult(MutationLeaseResult.TimedOut(maxWait));

        var lease = new FakeMutationLease(Name, OwnerLabel, NextGrantIsAbandoned);
        NextGrantIsAbandoned = false;
        _current = lease;
        return Task.FromResult(MutationLeaseResult.Held(lease));
    }
}
