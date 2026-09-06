using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Tests.Coordination;

public sealed class MutationCoordinatorTests
{
    private static Task<OperationResult<bool>> Clear(IMutationLease lease, CancellationToken token)
        => Task.FromResult(OperationResult<bool>.Success(true));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryAcquisitionRecoversBeforeOperationAndReleases(bool abandoned)
    {
        var provider = new FakeMutationLeaseProvider { NextGrantIsAbandoned = abandoned };
        var events = new List<string>();
        var leases = new List<FakeMutationLease>();
        var coordinator = new MutationCoordinator(provider, (lease, _) =>
        {
            Assert.True(lease.RequiresRecovery);
            Assert.False(lease.CanWrite);
            events.Add("recovery");
            var fake = Assert.IsType<FakeMutationLease>(lease);
            fake.OnRelease = () => events.Add("release");
            leases.Add(fake);
            return Clear(lease, default);
        });
        for (var i = 0; i < 2; i++)
        {
            var result = await coordinator.RunAsync(TimeSpan.Zero, (lease, _) =>
            {
                Assert.True(lease.CanWrite);
                events.Add("operation");
                return Task.FromResult(42);
            });
            Assert.True(result.OperationRan);
            Assert.Equal(42, result.Value);
        }
        Assert.Equal(["recovery", "operation", "release", "recovery", "operation", "release"], events);
        Assert.Equal(2, provider.Requests.Count);
        Assert.All(leases, lease => Assert.Equal(1, lease.ReleaseCount));
    }

    [Fact]
    public async Task RecoveryFailurePreservesDetailsAndDoesNotClear()
    {
        var provider = new FakeMutationLeaseProvider();
        var failure = OperationResult<bool>.Failure("Corrupt evidence", ErrorCategory.AccessDenied, new IOException("detail"));
        FakeMutationLease? held = null;
        var coordinator = new MutationCoordinator(provider, (lease, _) =>
        {
            held = (FakeMutationLease)lease;
            return Task.FromResult(failure);
        });
        var result = await coordinator.RunAsync<int>(TimeSpan.Zero, (_, _) => throw new InvalidOperationException("Must not run"));
        Assert.Same(failure, result.Recovery);
        Assert.False(result.OperationRan);
        Assert.True(held!.RequiresRecovery);
        Assert.Equal(1, held.ReleaseCount);
    }

    [Fact]
    public async Task NegativeClearanceDoesNotAuthorizeOperation()
    {
        var coordinator = new MutationCoordinator(new FakeMutationLeaseProvider(),
            (_, _) => Task.FromResult(OperationResult<bool>.Success(false)));
        var result = await coordinator.RunAsync<int>(TimeSpan.Zero, (_, _) => throw new InvalidOperationException("Must not run"));
        Assert.False(result.OperationRan);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallbackExceptionReleasesAndPreservesException(bool duringRecovery)
    {
        var provider = new FakeMutationLeaseProvider();
        FakeMutationLease? held = null;
        var failure = new IOException("callback detail");
        var coordinator = new MutationCoordinator(provider, (lease, _) =>
        {
            held = (FakeMutationLease)lease;
            return duringRecovery ? Task.FromException<OperationResult<bool>>(failure) : Clear(lease, default);
        });
        var thrown = await Assert.ThrowsAsync<IOException>(() => coordinator.RunAsync<int>(TimeSpan.Zero,
            (_, _) => Task.FromException<int>(failure)));
        Assert.Same(failure, thrown);
        Assert.Equal(1, held!.ReleaseCount);
        Assert.Equal(duringRecovery, held.RequiresRecovery);
    }

    [Fact]
    public async Task NestedCoordinatorWithSameNameFailsBeforeAcquiring()
    {
        var provider = new FakeMutationLeaseProvider();
        var outer = new MutationCoordinator(provider, Clear);
        var inner = new MutationCoordinator(provider, Clear);
        var result = await outer.RunAsync(TimeSpan.Zero, async (_, _) =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => inner.RunAsync(TimeSpan.Zero, (_, _) => Task.FromResult(1)));
            return 2;
        });
        Assert.Equal(2, result.Value);
        Assert.Single(provider.Requests);
        Assert.Null(provider.CurrentLease);
        Assert.True((await inner.RunAsync(TimeSpan.Zero, (_, _) => Task.FromResult(1))).OperationRan);
    }

    [Fact]
    public async Task IndependentCallCannotEnterHeldOperation()
    {
        var provider = new FakeMutationLeaseProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new MutationCoordinator(provider, Clear);
        var first = coordinator.RunAsync(TimeSpan.Zero, async (_, _) =>
        {
            entered.SetResult();
            await finish.Task;
            return 1;
        });
        await entered.Task;
        var second = await coordinator.RunAsync(TimeSpan.Zero, (_, _) => Task.FromResult(2));
        Assert.False(second.OperationRan);
        Assert.Equal(MutationLeaseOutcome.TimedOut, second.Acquisition.Outcome);
        finish.SetResult();
        Assert.True((await first).OperationRan);
    }

    [Fact]
    public async Task CancelledWaitPreservesProviderOutcome()
    {
        var provider = new FakeMutationLeaseProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var coordinator = new MutationCoordinator(provider, (_, _) => throw new InvalidOperationException("Recovery must not run"));
        var result = await coordinator.RunAsync(TimeSpan.Zero, (_, _) => Task.FromResult(1), cancellation.Token);
        Assert.Equal(MutationLeaseOutcome.Cancelled, result.Acquisition.Outcome);
        Assert.False(result.OperationRan);
    }

    [Fact]
    public async Task CancellationDuringRecoveryReleasesWithoutClearance()
    {
        var provider = new FakeMutationLeaseProvider();
        using var cancellation = new CancellationTokenSource();
        FakeMutationLease? held = null;
        var coordinator = new MutationCoordinator(provider, (lease, _) =>
        {
            held = (FakeMutationLease)lease;
            cancellation.Cancel();
            return Clear(lease, default);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunAsync(TimeSpan.Zero,
            (_, _) => Task.FromResult(1), cancellation.Token));
        Assert.True(held!.RequiresRecovery);
        Assert.Equal(1, held.ReleaseCount);
    }

    [Fact]
    public async Task AcquisitionRefusalRetainsNativeDiagnostics()
    {
        var provider = new FakeMutationLeaseProvider();
        var refusal = MutationLeaseResult.Faulted("native detail", 5, new IOException("inner"));
        provider.Script(() => refusal);
        var coordinator = new MutationCoordinator(provider, Clear);
        var result = await coordinator.RunAsync(TimeSpan.Zero, (_, _) => Task.FromResult(1));
        Assert.Same(refusal, result.Acquisition);
        Assert.False(result.OperationRan);
    }
}
