using ThisIsMyPC.Core.Coordination;

namespace ThisIsMyPC.Core.Tests.Coordination;

public class MutationLeaseContractTests
{
    [Fact]
    public void FreshLease_IsHeld_ButCannotWriteUntilCleared()
    {
        using var lease = new FakeMutationLease(@"Local\x", "app", abandoned: false);

        Assert.True(lease.IsHeld);
        Assert.False(lease.WasAbandoned);
        Assert.True(lease.RequiresRecovery);
        Assert.False(lease.CanWrite);
        Assert.NotEqual(Guid.Empty, lease.LeaseId);
        Assert.Equal(@"Local\x", lease.Name);
        Assert.Equal("app", lease.OwnerLabel);
        Assert.InRange(lease.AcquiredAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));

        lease.MarkRecovered();

        Assert.False(lease.RequiresRecovery);
        Assert.True(lease.CanWrite);
    }

    [Fact]
    public void AbandonedLease_HasTheSameGate_AndKeepsTheDiagnostic()
    {
        using var lease = new FakeMutationLease(@"Local\x", "service", abandoned: true);

        Assert.True(lease.WasAbandoned);
        Assert.True(lease.RequiresRecovery);
        Assert.False(lease.CanWrite);

        lease.MarkRecovered();

        Assert.True(lease.WasAbandoned);
        Assert.False(lease.RequiresRecovery);
        Assert.True(lease.CanWrite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MarkRecovered_Twice_Throws(bool abandoned)
    {
        using var lease = new FakeMutationLease(@"Local\x", "service", abandoned);
        lease.MarkRecovered();

        Assert.Throws<InvalidOperationException>(lease.MarkRecovered);
        Assert.True(lease.CanWrite);
    }

    [Fact]
    public void Dispose_ReleasesOnce_AndEndsHold()
    {
        var lease = new FakeMutationLease(@"Local\x", "app", abandoned: false);
        lease.MarkRecovered();

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(1, lease.ReleaseCount);
        Assert.False(lease.IsHeld);
        Assert.False(lease.CanWrite);
    }

    [Fact]
    public void Dispose_WithoutClearance_StillReleases()
    {
        var lease = new FakeMutationLease(@"Local\x", "app", abandoned: false);

        lease.Dispose();

        Assert.Equal(1, lease.ReleaseCount);
        Assert.False(lease.IsHeld);
        Assert.True(lease.RequiresRecovery);
        Assert.False(lease.CanWrite);
    }

    [Fact]
    public async Task DisposeAsync_ThenDispose_ReleasesOnce()
    {
        var lease = new FakeMutationLease(@"Local\x", "app", abandoned: false);

        await lease.DisposeAsync();
        lease.Dispose();
        await lease.DisposeAsync();

        Assert.Equal(1, lease.ReleaseCount);
        Assert.False(lease.IsHeld);
    }

    [Fact]
    public void DisposedLease_RefusesMarkRecovered()
    {
        var lease = new FakeMutationLease(@"Local\x", "service", abandoned: true);
        lease.Dispose();

        Assert.True(lease.RequiresRecovery);
        Assert.False(lease.CanWrite);
        Assert.Throws<InvalidOperationException>(lease.MarkRecovered);
    }

    [Fact]
    public void ConcurrentDispose_ReleasesOnce()
    {
        var lease = new FakeMutationLease(@"Local\x", "app", abandoned: false);
        using var gate = new ManualResetEventSlim(false);
        var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
        {
            gate.Wait();
            lease.Dispose();
        })).ToArray();

        foreach (var t in threads)
            t.Start();
        gate.Set();
        foreach (var t in threads)
            t.Join();

        Assert.Equal(1, lease.ReleaseCount);
    }

    [Theory]
    [InlineData("", "app")]
    [InlineData(" ", "app")]
    [InlineData(@"Local\x", "")]
    [InlineData(@"Local\x", " ")]
    public void Constructor_RejectsBlankNameOrLabel(string name, string label)
    {
        Assert.Throws<ArgumentException>(() => new FakeMutationLease(name, label, abandoned: false));
    }
}

public class MutationLeaseResultTests
{
    [Fact]
    public void Held_CleanLease_IsAcquired_ButNotWritableUntilCleared()
    {
        using var lease = new FakeMutationLease(@"Local\x", "app", abandoned: false);

        var result = MutationLeaseResult.Held(lease);

        Assert.Equal(MutationLeaseOutcome.Acquired, result.Outcome);
        Assert.Same(lease, result.Lease);
        Assert.True(result.IsAcquired);
        Assert.False(result.CanWrite);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.NativeErrorCode);
        Assert.Null(result.Exception);

        lease.MarkRecovered();

        Assert.True(result.CanWrite);
    }

    [Fact]
    public void Held_AbandonedLease_ReportsAbandoned_WithTheSameGate()
    {
        using var lease = new FakeMutationLease(@"Local\x", "service", abandoned: true);

        var result = MutationLeaseResult.Held(lease);

        Assert.Equal(MutationLeaseOutcome.AcquiredAbandoned, result.Outcome);
        Assert.True(result.IsAcquired);
        Assert.False(result.CanWrite);

        lease.MarkRecovered();

        Assert.Equal(MutationLeaseOutcome.AcquiredAbandoned, result.Outcome);
        Assert.True(result.CanWrite);
    }

    [Fact]
    public void Held_DisposedLease_Throws()
    {
        var lease = new FakeMutationLease(@"Local\x", "app", abandoned: false);
        lease.Dispose();

        Assert.Throws<ArgumentException>(() => MutationLeaseResult.Held(lease));
        Assert.Throws<ArgumentNullException>(() => MutationLeaseResult.Held(null!));
    }

    [Fact]
    public void NonAcquiredOutcomes_CarryNoLease()
    {
        var ex = new InvalidOperationException("boom");

        var timedOut = MutationLeaseResult.TimedOut(TimeSpan.FromMilliseconds(250));
        var cancelled = MutationLeaseResult.Cancelled();
        var refused = MutationLeaseResult.Refused("squatted", 5);
        var faulted = MutationLeaseResult.Faulted("wait failed", 6, ex);

        Assert.Equal(MutationLeaseOutcome.TimedOut, timedOut.Outcome);
        Assert.Contains("250", timedOut.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(MutationLeaseOutcome.Cancelled, cancelled.Outcome);
        Assert.Equal(MutationLeaseOutcome.Refused, refused.Outcome);
        Assert.Equal(5, refused.NativeErrorCode);
        Assert.Equal(MutationLeaseOutcome.Faulted, faulted.Outcome);
        Assert.Equal(6, faulted.NativeErrorCode);
        Assert.Same(ex, faulted.Exception);

        foreach (var result in new[] { timedOut, cancelled, refused, faulted })
        {
            Assert.Null(result.Lease);
            Assert.False(result.IsAcquired);
            Assert.False(result.CanWrite);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RefusedAndFaulted_RequireAMessage(string message)
    {
        Assert.Throws<ArgumentException>(() => MutationLeaseResult.Refused(message));
        Assert.Throws<ArgumentException>(() => MutationLeaseResult.Faulted(message));
    }
}

public class MutationLeaseNamesTests
{
    [Fact]
    public void ProductionName_IsGlobalAndValid()
    {
        Assert.StartsWith(@"Global\", MutationLeaseNames.Production, StringComparison.Ordinal);
        Assert.True(MutationLeaseNames.IsValid(MutationLeaseNames.Production));
    }

    [Fact]
    public void TestNames_AreLocalUniqueAndNeverProduction()
    {
        string a = MutationLeaseNames.ForTest();
        string b = MutationLeaseNames.ForTest();

        Assert.StartsWith(@"Local\", a, StringComparison.Ordinal);
        Assert.NotEqual(a, b);
        Assert.NotEqual(MutationLeaseNames.Production, a);
        Assert.True(MutationLeaseNames.IsValid(a));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"Global\")]
    [InlineData(@"Local\")]
    [InlineData("ThisIsMyPC.Lease")]
    [InlineData(@"Session\1\ThisIsMyPC")]
    [InlineData(@"Global\a\b")]
    [InlineData(@"Global\with space")]
    [InlineData("Global\\tab\t")]
    [InlineData(@"global\lowercase")]
    public void IsValid_RejectsMalformedNames(string? name)
    {
        Assert.False(MutationLeaseNames.IsValid(name));
    }

    [Fact]
    public void IsValid_RejectsOverlongNames()
    {
        Assert.False(MutationLeaseNames.IsValid(@"Global\" + new string('a', 201)));
        Assert.True(MutationLeaseNames.IsValid(@"Global\" + new string('a', 200)));
    }
}

public class FakeMutationLeaseProviderTests
{
    [Fact]
    public async Task Grant_Release_Grant_Lifecycle()
    {
        var provider = new FakeMutationLeaseProvider();

        var first = await provider.AcquireAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(MutationLeaseOutcome.Acquired, first.Outcome);
        Assert.False(first.CanWrite);
        Assert.Same(first.Lease, provider.CurrentLease);

        var blocked = await provider.AcquireAsync(TimeSpan.FromMilliseconds(5));
        Assert.Equal(MutationLeaseOutcome.TimedOut, blocked.Outcome);

        first.Lease!.MarkRecovered();
        Assert.True(first.CanWrite);
        first.Lease.Dispose();
        Assert.Null(provider.CurrentLease);

        var second = await provider.AcquireAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(MutationLeaseOutcome.Acquired, second.Outcome);
        Assert.NotEqual(first.Lease.LeaseId, second.Lease!.LeaseId);
        Assert.False(second.CanWrite);
        second.Lease.Dispose();

        Assert.Equal(3, provider.Requests.Count);
    }

    [Fact]
    public async Task ReleaseWithoutClearance_DoesNotClearTheNextHolder()
    {
        var provider = new FakeMutationLeaseProvider();

        var first = await provider.AcquireAsync(TimeSpan.FromSeconds(1));
        first.Lease!.Dispose();

        var second = await provider.AcquireAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(MutationLeaseOutcome.Acquired, second.Outcome);
        Assert.True(second.Lease!.RequiresRecovery);
        Assert.False(second.CanWrite);
        second.Lease.Dispose();
    }

    [Fact]
    public async Task AbandonedGrant_IsDiagnosticOnly()
    {
        var provider = new FakeMutationLeaseProvider { NextGrantIsAbandoned = true };

        var abandoned = await provider.AcquireAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(MutationLeaseOutcome.AcquiredAbandoned, abandoned.Outcome);
        Assert.True(abandoned.Lease!.WasAbandoned);
        Assert.False(abandoned.CanWrite);
        abandoned.Lease.MarkRecovered();
        Assert.True(abandoned.CanWrite);
        abandoned.Lease.Dispose();

        var next = await provider.AcquireAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(MutationLeaseOutcome.Acquired, next.Outcome);
        Assert.False(next.Lease!.WasAbandoned);
        Assert.False(next.CanWrite);
        next.Lease.Dispose();
    }

    [Fact]
    public async Task CancelledToken_ReturnsCancelled_NotAnException()
    {
        var provider = new FakeMutationLeaseProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await provider.AcquireAsync(TimeSpan.FromSeconds(1), cts.Token);

        Assert.Equal(MutationLeaseOutcome.Cancelled, result.Outcome);
        Assert.Null(provider.CurrentLease);
    }

    [Fact]
    public async Task ScriptedOutcome_WinsOverGrant()
    {
        var provider = new FakeMutationLeaseProvider();
        provider.Script(() => MutationLeaseResult.Refused("precreated object"));

        var refused = await provider.AcquireAsync(TimeSpan.FromSeconds(1));
        var granted = await provider.AcquireAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(MutationLeaseOutcome.Refused, refused.Outcome);
        Assert.Equal(MutationLeaseOutcome.Acquired, granted.Outcome);
        granted.Lease!.Dispose();
    }
}
