using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Tests.Fakes;

namespace ThisIsMyPC.Core.Tests.Services;

/// <summary>
/// PendingChangesService on the shared executor: two queues are isolated, both
/// route through one executor, group rollback swaps descriptors, and the
/// current exception and cancellation behavior is pinned so a later change to
/// it is deliberate.
/// </summary>
public sealed class PendingChangesSharedExecutorTests
{
    private static ChangeDescriptor Change(string settingId, SettingEnforcement? enforcement = null) => new()
    {
        ModuleId = "TestModule",
        SettingId = settingId,
        DisplayName = $"Change {settingId}",
        SystemLocation = $@"HKCU\Test\{settingId}",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "0",
        AfterDisplay = "1",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Modify,
        Enforcement = enforcement,
    };

    private static ChangeGroup Group(string groupId, params ChangeDescriptor[] changes) => new()
    {
        GroupId = groupId,
        DisplayName = $"Group {groupId}",
        Description = $"Test group {groupId}",
        Changes = changes,
    };

    private static Func<ChangeDescriptor, Task<OperationResult<bool>>> Recording(
        List<ChangeDescriptor> into, Func<ChangeDescriptor, bool>? succeeds = null) =>
        c =>
        {
            into.Add(c);
            return Task.FromResult((succeeds?.Invoke(c) ?? true)
                ? OperationResult<bool>.Success(true)
                : OperationResult<bool>.Failure("apply failed", ErrorCategory.AccessDenied));
        };

    [Fact]
    public void Create_rejects_a_null_executor()
    {
        Assert.Throws<ArgumentNullException>(() => PendingChangesService.Create(null!));
    }

    [Fact]
    public async Task Two_queues_on_one_executor_stage_and_apply_independently()
    {
        var executor = new CountingExecutor();
        var appQueue = PendingChangesService.Create(executor);
        var restorationQueue = PendingChangesService.Create(executor);
        var appApplied = new List<ChangeDescriptor>();
        var restorationApplied = new List<ChangeDescriptor>();

        appQueue.Stage(Change("app-a"));
        appQueue.Stage(Change("app-b"));
        restorationQueue.Stage(Change("restore-a"));

        Assert.Equal(2, appQueue.PendingCount);
        Assert.Equal(1, restorationQueue.PendingCount);

        var restorationResult = await restorationQueue.ApplyAllAsync(Recording(restorationApplied), Recording([]));

        Assert.True(restorationResult.IsSuccess);
        Assert.Equal(["restore-a"], restorationApplied.Select(c => c.SettingId));
        Assert.Equal(0, restorationQueue.PendingCount);
        Assert.Equal(2, appQueue.PendingCount);
        Assert.Empty(appApplied);
        Assert.Equal(1, executor.ApplyCalls);

        restorationQueue.DiscardAll();
        Assert.Equal(2, appQueue.PendingCount);

        var appResult = await appQueue.ApplyAllAsync(Recording(appApplied), Recording([]));
        Assert.True(appResult.IsSuccess);
        Assert.Equal(["app-a", "app-b"], appApplied.Select(c => c.SettingId));
        Assert.Equal(3, executor.ApplyCalls);
    }

    [Fact]
    public void Unstage_on_one_queue_never_touches_the_other()
    {
        var executor = new ReversibleChangeExecutor();
        var first = PendingChangesService.Create(executor);
        var second = PendingChangesService.Create(executor);
        first.Stage(Group("shared-id", Change("a")));
        second.Stage(Group("shared-id", Change("b")));

        first.Unstage("shared-id");

        Assert.Equal(0, first.PendingCount);
        Assert.Equal(1, second.PendingCount);
        Assert.Equal("b", second.PendingGroups[0].Changes[0].SettingId);
    }

    [Fact]
    public async Task Queue_created_from_an_executor_routes_enforced_changes_through_it()
    {
        var enforcement = new FakeEnforcementExecutor();
        var queue = PendingChangesService.Create(new ReversibleChangeExecutor(enforcement));
        queue.Stage(Group("g", Change("plain"), Change("enforced", new SettingEnforcement { CompanionServices = ["WSearch"] })));
        var applied = new List<ChangeDescriptor>();

        var result = await queue.ApplyAllAsync(Recording(applied), Recording([]));

        Assert.True(result.IsSuccess);
        Assert.Equal(["plain"], applied.Select(c => c.SettingId));
        Assert.Equal(["enforced"], enforcement.ExecutedChanges.Select(c => c.SettingId));
    }

    [Fact]
    public async Task Queue_created_from_an_executor_without_enforcement_throws_before_any_write()
    {
        var queue = PendingChangesService.Create(new ReversibleChangeExecutor());
        queue.Stage(Change("plain"));
        queue.Stage(Change("enforced", new SettingEnforcement { CompanionServices = ["WSearch"] }));
        var applied = new List<ChangeDescriptor>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queue.ApplyAllAsync(Recording(applied), Recording([])));

        Assert.Empty(applied);
        Assert.Equal(2, queue.PendingCount);
        Assert.False(queue.IsApplying);
    }

    [Fact]
    public async Task Group_rollback_hands_the_executor_swapped_descriptors_in_reverse_order()
    {
        var executor = new CountingExecutor();
        var queue = PendingChangesService.Create(executor);
        queue.Stage(Group("g", Change("a"), Change("b"), Change("fail")));
        var reverted = new List<ChangeDescriptor>();

        var result = await queue.ApplyAllAsync(
            Recording([], c => c.SettingId != "fail"),
            Recording(reverted));

        Assert.False(result.IsSuccess);
        Assert.Equal("fail", result.Failed?.SettingId);
        Assert.Equal(["b", "a"], reverted.Select(c => c.SettingId));
        Assert.All(reverted, c =>
        {
            Assert.Equal("1", c.BeforeValue);
            Assert.Equal("0", c.AfterValue);
        });
        Assert.Equal(2, executor.RevertCalls);
        Assert.Equal(["b", "a"], result.RolledBack.Select(c => c.SettingId));
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public async Task Rollback_failure_is_reported_in_RolledBack_and_the_group_stays_pending()
    {
        var queue = PendingChangesService.Create(new ReversibleChangeExecutor());
        queue.Stage(Group("g", Change("a"), Change("b"), Change("fail")));

        var result = await queue.ApplyAllAsync(
            Recording([], c => c.SettingId != "fail"),
            c => Task.FromResult(c.SettingId == "b"
                ? OperationResult<bool>.Failure("stuck", ErrorCategory.AccessDenied)
                : OperationResult<bool>.Success(true)));

        Assert.False(result.IsSuccess);
        Assert.Equal(["a"], result.RolledBack.Select(c => c.SettingId));
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public async Task Delegate_exception_mid_batch_propagates_and_leaves_the_queue_as_it_was()
    {
        // Pins current behavior: an exception (including cancellation raised inside a
        // delegate) is not a failed result, so no rollback runs and no group is
        // removed. IsApplying still resets. A cancellation-aware batch is a separate,
        // deliberate change; see the restoration plan.
        var queue = PendingChangesService.Create(new ReversibleChangeExecutor());
        queue.Stage(Group("g1", Change("first")));
        queue.Stage(Group("g2", Change("boom")));
        var reverted = new List<ChangeDescriptor>();

        await Assert.ThrowsAsync<OperationCanceledException>(() => queue.ApplyAllAsync(
            c => c.SettingId == "boom"
                ? throw new OperationCanceledException()
                : Task.FromResult(OperationResult<bool>.Success(true)),
            Recording(reverted)));

        Assert.False(queue.IsApplying);
        Assert.Empty(reverted);
        Assert.Equal(2, queue.PendingCount);
    }

    [Fact]
    public async Task IsApplying_is_true_during_the_batch_and_false_after()
    {
        var queue = PendingChangesService.Create(new ReversibleChangeExecutor());
        queue.Stage(Change("a"));
        bool? observed = null;

        await queue.ApplyAllAsync(
            _ =>
            {
                observed = queue.IsApplying;
                return Task.FromResult(OperationResult<bool>.Success(true));
            },
            Recording([]));

        Assert.True(observed);
        Assert.False(queue.IsApplying);
    }

    /// <summary>Counts calls so a test can prove both queues share one executor instance.</summary>
    private sealed class CountingExecutor : IReversibleChangeExecutor
    {
        private readonly ReversibleChangeExecutor _inner = new();

        public int ApplyCalls { get; private set; }
        public int RevertCalls { get; private set; }

        public bool CanExecute(ChangeDescriptor change) => _inner.CanExecute(change);

        public Task<OperationResult<bool>> ApplyAsync(
            ChangeDescriptor change,
            Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc)
        {
            ApplyCalls++;
            return _inner.ApplyAsync(change, applyFunc);
        }

        public Task<OperationResult<bool>> RevertAsync(
            ChangeDescriptor change,
            Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc)
        {
            RevertCalls++;
            return _inner.RevertAsync(change, revertFunc);
        }
    }
}
