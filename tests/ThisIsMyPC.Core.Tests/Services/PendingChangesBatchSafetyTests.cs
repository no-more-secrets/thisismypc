using System.ComponentModel;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Services;

/// <summary>
/// The batch contract a background restore relies on: a thrown apply is an
/// uncertain, reported failure; cancellation stops new applies, lets the in-flight
/// call finish, and rolls the current group back without the cancelled token;
/// rollback failures are captured one by one; finished groups are committed on
/// every exit and never replayed; queues never share state.
/// </summary>
public sealed class PendingChangesBatchSafetyTests
{
    private static ChangeDescriptor Change(string settingId, RestartRequirement restart = RestartRequirement.None) => new()
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
        RestartRequirement = restart,
    };

    private static ChangeGroup Group(string groupId, params ChangeDescriptor[] changes) => new()
    {
        GroupId = groupId,
        DisplayName = $"Group {groupId}",
        Description = $"Test group {groupId}",
        Changes = changes,
    };

    private static Task<OperationResult<bool>> Ok() => Task.FromResult(OperationResult<bool>.Success(true));

    private static Func<ChangeDescriptor, Task<OperationResult<bool>>> Recording(List<string> into) =>
        c =>
        {
            into.Add(c.SettingId);
            return Ok();
        };

    private static PendingChangesService Queue() => PendingChangesService.Create(new ReversibleChangeExecutor());

    // ---- apply throws -------------------------------------------------------

    [Fact]
    public async Task Apply_throw_rolls_back_the_groups_earlier_changes_and_leaves_the_thrown_one_alone()
    {
        var queue = Queue();
        queue.Stage(Group("g1", Change("done")));
        queue.Stage(Group("g2", Change("a"), Change("b"), Change("boom"), Change("never")));
        var applied = new List<string>();
        var reverted = new List<string>();

        var result = await queue.ApplyAllAsync(
            c =>
            {
                applied.Add(c.SettingId);
                return c.SettingId == "boom" ? throw new InvalidOperationException("registry exploded") : Ok();
            },
            Recording(reverted));

        Assert.False(result.IsSuccess);
        Assert.Equal(MutationFailureKind.ChangeThrew, result.FailureKind);
        Assert.True(result.HasUncertainState);
        Assert.Equal("boom", result.Failed?.SettingId);
        Assert.IsType<InvalidOperationException>(result.Exception);
        Assert.Contains("registry exploded", result.ErrorMessage);
        Assert.Equal(ErrorCategory.ServiceUnavailable, result.ErrorCategory);
        Assert.Equal(["done", "a", "b", "boom"], applied);
        Assert.Equal(["b", "a"], reverted);
        Assert.Equal(["b", "a"], result.RolledBack.Select(c => c.SettingId));
        Assert.DoesNotContain(result.RolledBack, c => c.SettingId == "boom");
        Assert.Empty(result.RollbackFailures);
        Assert.Equal(["boom"], result.Uncertain.Select(c => c.SettingId));
        Assert.Equal(["done"], result.Applied.Select(c => c.SettingId));
        Assert.Equal(["g2"], queue.PendingGroups.Select(g => g.GroupId));
        var record = Assert.Single(queue.ReconciliationRequired);
        Assert.Equal("g2", record.Group.GroupId);
        Assert.Equal(MutationFailureKind.ChangeThrew, record.Kind);
        Assert.Equal(["boom"], record.Uncertain.Select(c => c.SettingId));
        Assert.Equal(["b", "a"], record.RolledBack.Select(c => c.SettingId));
        Assert.Same(result.Exception, record.Exception);
        Assert.False(queue.IsApplying);
    }

    [Fact]
    public async Task Apply_throw_on_the_first_change_of_the_first_group_reports_nothing_applied()
    {
        var queue = Queue();
        queue.Stage(Group("g1", Change("boom")));
        var reverted = new List<string>();

        var result = await queue.ApplyAllAsync(
            _ => throw new IOException("locked"),
            Recording(reverted));

        Assert.Equal(MutationFailureKind.ChangeThrew, result.FailureKind);
        Assert.Empty(result.Applied);
        Assert.Empty(result.RolledBack);
        Assert.Empty(reverted);
        Assert.Equal(1, queue.PendingCount);
        Assert.False(queue.IsApplying);
    }

    [Fact]
    public async Task Apply_throw_keeps_restarts_from_the_committed_groups_only()
    {
        var queue = Queue();
        queue.Stage(Group("g1", Change("done", RestartRequirement.ExplorerRestart)));
        queue.Stage(Group("g2", Change("a", RestartRequirement.Reboot), Change("boom", RestartRequirement.SignOut)));

        var result = await queue.ApplyAllAsync(
            c => c.SettingId == "boom" ? throw new InvalidOperationException() : Ok(),
            _ => Ok());

        Assert.Equal([RestartRequirement.ExplorerRestart], result.RequiredRestarts);
    }

    // ---- rollback failures --------------------------------------------------

    [Fact]
    public async Task Rollback_throw_is_captured_and_the_remaining_rollbacks_still_run()
    {
        var queue = Queue();
        queue.Stage(Group("g", Change("a"), Change("b"), Change("c"), Change("fail")));
        var reverted = new List<string>();

        var result = await queue.ApplyAllAsync(
            c => c.SettingId == "fail"
                ? Task.FromResult(OperationResult<bool>.Failure("no", ErrorCategory.AccessDenied))
                : Ok(),
            c =>
            {
                reverted.Add(c.SettingId);
                return c.SettingId == "b" ? throw new InvalidOperationException("revert exploded") : Ok();
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(MutationFailureKind.ChangeFailed, result.FailureKind);
        Assert.Equal(["c", "b", "a"], reverted);
        Assert.Equal(["c", "a"], result.RolledBack.Select(c => c.SettingId));
        var failure = Assert.Single(result.RollbackFailures);
        Assert.Equal("b", failure.Change.SettingId);
        Assert.Equal("1", failure.Change.AfterValue);
        Assert.IsType<InvalidOperationException>(failure.Exception);
        Assert.True(result.HasUncertainState);
        Assert.Equal(["b", "fail"], result.Uncertain.Select(c => c.SettingId));
        Assert.Equal(1, queue.PendingCount);
        var record = Assert.Single(queue.ReconciliationRequired);
        Assert.Equal(["b", "fail"], record.Uncertain.Select(c => c.SettingId));
        Assert.Equal(["c", "a"], record.RolledBack.Select(c => c.SettingId));
        Assert.Same(failure, Assert.Single(record.RollbackFailures));
        Assert.False(queue.IsApplying);
    }

    [Fact]
    public async Task Rollback_failure_result_is_uncertain_like_a_throw()
    {
        // A revert that returns failure may have written part of the value first;
        // a null exception is diagnostics, not evidence about the live state.
        var queue = Queue();
        queue.Stage(Group("g", Change("a"), Change("fail")));

        var result = await queue.ApplyAllAsync(
            c => c.SettingId == "fail"
                ? Task.FromResult(OperationResult<bool>.Failure("no", ErrorCategory.AccessDenied))
                : Ok(),
            _ => Task.FromResult(OperationResult<bool>.Failure("stuck", ErrorCategory.ProtectedByPolicy)));

        var failure = Assert.Single(result.RollbackFailures);
        Assert.Equal("a", failure.Change.SettingId);
        Assert.Equal("stuck", failure.ErrorMessage);
        Assert.Null(failure.Exception);
        Assert.True(result.HasUncertainState);
        Assert.Equal(["a", "fail"], result.Uncertain.Select(c => c.SettingId));
        Assert.Empty(result.RolledBack);
        Assert.Single(queue.ReconciliationRequired);
    }

    [Fact]
    public async Task Failed_result_marks_the_group_even_when_every_rollback_succeeded()
    {
        // The failed change itself is uncertain: a module reports failure after
        // catching its own exception, and enforcement can fail after the primary
        // write landed. Its before value may no longer describe the machine.
        var queue = Queue();
        queue.Stage(Group("g", Change("a"), Change("fail")));

        var result = await queue.ApplyAllAsync(
            c => c.SettingId == "fail"
                ? Task.FromResult(OperationResult<bool>.Failure("no", ErrorCategory.AccessDenied))
                : Ok(),
            _ => Ok());

        Assert.Equal(MutationFailureKind.ChangeFailed, result.FailureKind);
        Assert.Equal(["a"], result.RolledBack.Select(c => c.SettingId));
        Assert.Empty(result.RollbackFailures);
        Assert.Equal(["fail"], result.Uncertain.Select(c => c.SettingId));
        Assert.True(result.HasUncertainState);
        var record = Assert.Single(queue.ReconciliationRequired);
        Assert.Equal(MutationFailureKind.ChangeFailed, record.Kind);
        Assert.Equal("fail", record.Failed?.SettingId);
        Assert.Equal("no", record.ErrorMessage);
    }

    // ---- cancellation -------------------------------------------------------

    [Fact]
    public async Task Cancelled_before_the_batch_applies_nothing_and_keeps_the_queue()
    {
        var queue = Queue();
        queue.Stage(Group("g1", Change("a")));
        queue.Stage(Group("g2", Change("b")));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var applied = new List<string>();
        var reverted = new List<string>();
        var isApplyingSeen = new List<bool>();
        queue.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(queue.IsApplying))
                isApplyingSeen.Add(queue.IsApplying);
        };

        var result = await queue.ApplyAllAsync(Recording(applied), Recording(reverted), cts.Token);

        Assert.False(result.IsSuccess);
        Assert.True(result.WasCancelled);
        Assert.Equal(MutationFailureKind.Cancelled, result.FailureKind);
        Assert.Null(result.Failed);
        Assert.False(result.HasUncertainState);
        Assert.Empty(applied);
        Assert.Empty(reverted);
        Assert.Empty(result.Applied);
        Assert.Empty(result.RolledBack);
        Assert.Equal(["g1", "g2"], queue.PendingGroups.Select(g => g.GroupId));
        Assert.Equal([true, false], isApplyingSeen);
        Assert.False(queue.IsApplying);
    }

    [Fact]
    public async Task Cancelled_during_the_first_change_finishes_it_then_rolls_it_back()
    {
        var queue = Queue();
        queue.Stage(Group("g", Change("a"), Change("b")));
        using var cts = new CancellationTokenSource();
        var applied = new List<string>();
        var reverted = new List<string>();

        var result = await queue.ApplyAllAsync(
            c =>
            {
                applied.Add(c.SettingId);
                cts.Cancel();
                return Ok();
            },
            Recording(reverted),
            cts.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(["a"], applied);
        Assert.Equal(["a"], reverted);
        Assert.Equal(["a"], result.RolledBack.Select(c => c.SettingId));
        Assert.Empty(result.Applied);
        Assert.Null(result.Failed);
        Assert.Empty(result.Uncertain);
        Assert.False(result.HasUncertainState);
        Assert.Equal(1, queue.PendingCount);
        // Every revert returned success, so the group is clean and stays retryable.
        Assert.Empty(queue.ReconciliationRequired);
    }

    [Fact]
    public async Task Cancelled_between_changes_awaits_the_in_flight_call_before_rolling_back()
    {
        var queue = Queue();
        queue.Stage(Group("g1", Change("done")));
        queue.Stage(Group("g2", Change("a"), Change("slow"), Change("never")));
        using var cts = new CancellationTokenSource();
        var slowStarted = new TaskCompletionSource();
        var release = new TaskCompletionSource<OperationResult<bool>>();
        var applied = new List<string>();
        var reverted = new List<string>();
        var revertSawTokenCancelled = new List<bool>();

        var batch = queue.ApplyAllAsync(
            c =>
            {
                applied.Add(c.SettingId);
                if (c.SettingId != "slow")
                    return Ok();
                slowStarted.SetResult();
                return release.Task;
            },
            c =>
            {
                reverted.Add(c.SettingId);
                revertSawTokenCancelled.Add(cts.Token.IsCancellationRequested);
                return Ok();
            },
            cts.Token);

        await slowStarted.Task;
        await cts.CancelAsync();

        // Cancellation does not abandon the in-flight call: the batch is still waiting.
        await Task.Delay(50);
        Assert.False(batch.IsCompleted);
        Assert.Empty(reverted);
        Assert.True(queue.IsApplying);

        release.SetResult(OperationResult<bool>.Success(true));
        var result = await batch;

        Assert.True(result.WasCancelled);
        Assert.Equal(["done", "a", "slow"], applied);
        Assert.Equal(["slow", "a"], reverted);
        Assert.All(revertSawTokenCancelled, Assert.True);
        Assert.Equal(["slow", "a"], result.RolledBack.Select(c => c.SettingId));
        Assert.Equal(["done"], result.Applied.Select(c => c.SettingId));
        Assert.Equal(["g2"], queue.PendingGroups.Select(g => g.GroupId));
        Assert.False(queue.IsApplying);
    }

    [Fact]
    public async Task Cancelled_after_a_group_completes_commits_that_group_and_stops_before_the_next()
    {
        var queue = Queue();
        queue.Stage(Group("g1", Change("a"), Change("b")));
        queue.Stage(Group("g2", Change("c")));
        using var cts = new CancellationTokenSource();
        var applied = new List<string>();
        var reverted = new List<string>();

        var result = await queue.ApplyAllAsync(
            c =>
            {
                applied.Add(c.SettingId);
                if (c.SettingId == "b")
                    cts.Cancel();
                return Ok();
            },
            Recording(reverted),
            cts.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(["a", "b"], applied);
        Assert.Empty(reverted);
        Assert.Empty(result.RolledBack);
        Assert.Equal(["a", "b"], result.Applied.Select(c => c.SettingId));
        Assert.Equal(["g2"], queue.PendingGroups.Select(g => g.GroupId));
    }

    [Fact]
    public async Task Cancelled_batch_with_a_failing_rollback_retains_the_failure()
    {
        var queue = Queue();
        queue.Stage(Group("g", Change("a"), Change("b"), Change("c")));
        using var cts = new CancellationTokenSource();

        var result = await queue.ApplyAllAsync(
            c =>
            {
                if (c.SettingId == "b")
                    cts.Cancel();
                return Ok();
            },
            c => c.SettingId == "a"
                ? throw new InvalidOperationException("revert exploded")
                : Ok(),
            cts.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(["b"], result.RolledBack.Select(c => c.SettingId));
        var failure = Assert.Single(result.RollbackFailures);
        Assert.Equal("a", failure.Change.SettingId);
        Assert.NotNull(failure.Exception);
        Assert.True(result.HasUncertainState);
        Assert.Equal(["a"], result.Uncertain.Select(c => c.SettingId));
        Assert.Equal(1, queue.PendingCount);
        var record = Assert.Single(queue.ReconciliationRequired);
        Assert.Equal(MutationFailureKind.Cancelled, record.Kind);
        Assert.Null(record.Failed);
    }

    [Fact]
    public async Task Delegate_that_throws_OperationCanceledException_for_the_batch_token_is_still_uncertain()
    {
        // The batch cannot tell whether the delegate checked the token before or after
        // writing, so it never treats a thrown OCE as a clean stop.
        var queue = Queue();
        queue.Stage(Group("g", Change("a"), Change("obeys-token")));
        using var cts = new CancellationTokenSource();
        var reverted = new List<string>();

        var result = await queue.ApplyAllAsync(
            c =>
            {
                if (c.SettingId != "obeys-token")
                    return Ok();
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
                return Ok();
            },
            Recording(reverted),
            cts.Token);

        Assert.Equal(MutationFailureKind.ChangeThrew, result.FailureKind);
        Assert.False(result.WasCancelled);
        Assert.True(result.HasUncertainState);
        Assert.Equal("obeys-token", result.Failed?.SettingId);
        Assert.Equal(["a"], reverted);
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public async Task Two_argument_overload_ignores_no_token_and_completes()
    {
        var queue = Queue();
        queue.Stage(Group("g", Change("a")));

        var result = await queue.ApplyAllAsync(_ => Ok(), _ => Ok());

        Assert.True(result.IsSuccess);
        Assert.Equal(MutationFailureKind.None, result.FailureKind);
        Assert.False(result.WasCancelled);
        Assert.False(result.HasUncertainState);
        Assert.Empty(result.RollbackFailures);
        Assert.Empty(result.Uncertain);
        Assert.Null(result.Exception);
        Assert.Empty(queue.ReconciliationRequired);
    }

    // ---- replay and isolation ----------------------------------------------

    [Fact]
    public async Task Retry_after_a_cancelled_batch_applies_only_the_groups_still_staged()
    {
        var queue = Queue();
        queue.Stage(Group("g1", Change("a")));
        queue.Stage(Group("g2", Change("b"), Change("c")));
        queue.Stage(Group("g3", Change("d")));
        using var cts = new CancellationTokenSource();
        var applied = new List<string>();

        var first = await queue.ApplyAllAsync(
            c =>
            {
                applied.Add(c.SettingId);
                if (c.SettingId == "b")
                    cts.Cancel();
                return Ok();
            },
            _ => Ok(),
            cts.Token);

        Assert.True(first.WasCancelled);
        Assert.Equal(["a"], first.Applied.Select(c => c.SettingId));
        Assert.Equal(["g2", "g3"], queue.PendingGroups.Select(g => g.GroupId));

        var second = await queue.ApplyAllAsync(Recording(applied), _ => Ok(), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(["a", "b", "b", "c", "d"], applied);
        Assert.Equal(["b", "c", "d"], second.Applied.Select(c => c.SettingId));
        Assert.Equal(0, queue.PendingCount);
    }

    // ---- reconciliation ---------------------------------------------------

    [Fact]
    public async Task Apply_after_a_thrown_apply_refuses_without_invoking_any_writer()
    {
        var queue = Queue();
        queue.Stage(Group("g1", Change("a")));
        queue.Stage(Group("g2", Change("boom")));
        queue.Stage(Group("g3", Change("clean")));
        var applied = new List<string>();
        var reverted = new List<string>();

        var first = await queue.ApplyAllAsync(
            c =>
            {
                applied.Add(c.SettingId);
                return c.SettingId == "boom" ? throw new InvalidOperationException("exploded") : Ok();
            },
            Recording(reverted));
        var second = await queue.ApplyAllAsync(Recording(applied), Recording(reverted));

        Assert.Equal(MutationFailureKind.ChangeThrew, first.FailureKind);
        Assert.False(second.IsSuccess);
        Assert.Equal(MutationFailureKind.ReconciliationRequired, second.FailureKind);
        Assert.Equal("boom", second.Failed?.SettingId);
        Assert.Equal(["boom"], second.Uncertain.Select(c => c.SettingId));
        Assert.True(second.HasUncertainState);
        Assert.Contains("Group g2", second.ErrorMessage);
        Assert.Empty(second.Applied);
        Assert.Empty(second.RolledBack);
        Assert.Equal(["a", "boom"], applied);
        Assert.Empty(reverted);
        // The clean group behind it is not applied around the marked one.
        Assert.Equal(["g2", "g3"], queue.PendingGroups.Select(g => g.GroupId));
        Assert.Single(queue.ReconciliationRequired);
        Assert.False(queue.IsApplying);
    }

    [Fact]
    public async Task Apply_after_a_failed_rollback_refuses_and_keeps_the_diagnostic_record()
    {
        var queue = Queue();
        queue.Stage(Group("g", Change("a"), Change("fail")));
        var applied = new List<string>();

        var first = await queue.ApplyAllAsync(
            c =>
            {
                applied.Add(c.SettingId);
                return c.SettingId == "fail"
                    ? Task.FromResult(OperationResult<bool>.Failure("no", ErrorCategory.AccessDenied))
                    : Ok();
            },
            _ => throw new InvalidOperationException("revert exploded"));
        var before = Assert.Single(queue.ReconciliationRequired);
        var second = await queue.ApplyAllAsync(Recording(applied), _ => Ok());
        var after = Assert.Single(queue.ReconciliationRequired);

        Assert.Equal(MutationFailureKind.ChangeFailed, first.FailureKind);
        Assert.Equal(MutationFailureKind.ReconciliationRequired, second.FailureKind);
        Assert.Equal(["a", "fail"], second.Uncertain.Select(c => c.SettingId));
        Assert.Single(second.RollbackFailures);
        Assert.Equal(["a", "fail"], applied);
        Assert.Same(before, after);
        Assert.Equal("revert exploded", Assert.Single(after.RollbackFailures).ErrorMessage);
    }

    [Fact]
    public async Task DiscardAll_clears_the_mark_and_a_fresh_group_under_the_same_id_applies()
    {
        var queue = Queue();
        queue.Stage(Group("g1", Change("boom")));
        queue.Stage(Group("g2", Change("other")));
        var applied = new List<string>();
        var reconciliationEvents = 0;
        queue.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(queue.ReconciliationRequired))
                reconciliationEvents++;
        };

        await queue.ApplyAllAsync(_ => throw new InvalidOperationException(), _ => Ok());
        Assert.Equal(1, reconciliationEvents);

        queue.DiscardAll();
        Assert.Equal(2, reconciliationEvents);
        Assert.Empty(queue.ReconciliationRequired);

        // Re-read and restage: same id, new instance, no inherited record.
        queue.Stage(Group("g1", Change("boom")));
        var result = await queue.ApplyAllAsync(Recording(applied), _ => Ok());

        Assert.True(result.IsSuccess);
        Assert.Equal(["boom"], applied);
        Assert.Equal(0, queue.PendingCount);
        Assert.Empty(queue.ReconciliationRequired);
    }

    [Fact]
    public async Task DiscardAll_clears_every_mark()
    {
        var queue = Queue();
        queue.Stage(Group("g1", Change("boom")));
        var reconciliationEvents = 0;
        queue.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(queue.ReconciliationRequired))
                reconciliationEvents++;
        };

        await queue.ApplyAllAsync(_ => throw new InvalidOperationException(), _ => Ok());
        queue.DiscardAll();

        Assert.Empty(queue.ReconciliationRequired);
        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(2, reconciliationEvents);

        queue.Stage(Group("g2", Change("a")));
        Assert.True((await queue.ApplyAllAsync(_ => Ok(), _ => Ok())).IsSuccess);
    }

    [Fact]
    public async Task Staging_under_a_marked_id_is_refused_and_the_mark_stays()
    {
        var queue = Queue();
        var marked = Group("g", Change("boom"));
        queue.Stage(marked);
        await queue.ApplyAllAsync(_ => throw new InvalidOperationException(), _ => Ok());
        var events = new List<string>();
        queue.PropertyChanged += (_, e) => events.Add(e.PropertyName!);

        Assert.Throws<InvalidOperationException>(() => queue.Stage(Group("g", Change("boom"))));
        Assert.Throws<InvalidOperationException>(() => queue.Stage(marked));

        var applied = new List<string>();
        var result = await queue.ApplyAllAsync(Recording(applied), _ => Ok());

        Assert.Equal(MutationFailureKind.ReconciliationRequired, result.FailureKind);
        Assert.Empty(applied);
        Assert.Equal(1, queue.PendingCount);
        Assert.Same(marked, Assert.Single(queue.ReconciliationRequired).Group);
        Assert.Empty(events);

        // The only way out is still to discard it; then the id is free again.
        queue.DiscardAll();
        queue.Stage(Group("g", Change("boom")));
        Assert.True((await queue.ApplyAllAsync(Recording(applied), _ => Ok())).IsSuccess);
        Assert.Equal(["boom"], applied);
    }

    [Fact]
    public async Task Queued_replacement_after_failure_cannot_remove_the_reconciliation_gate()
    {
        var queue = Queue();
        var original = Group("g", Change("boom"));
        queue.Stage(original);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = ReplaceAfterReleaseAsync();

        await queue.ApplyAllAsync(_ => throw new InvalidOperationException(), _ => Ok());
        var events = new List<string>();
        queue.PropertyChanged += (_, e) => events.Add(e.PropertyName!);
        releaseCallback.SetResult();
        await callback;

        Assert.Same(original, queue.PendingGroups[0]);
        Assert.Same(original, Assert.Single(queue.ReconciliationRequired).Group);
        Assert.DoesNotContain(nameof(queue.ReconciliationRequired), events);
        var applied = new List<string>();
        var result = await queue.ApplyAllAsync(Recording(applied), _ => Ok());
        Assert.Equal(MutationFailureKind.ReconciliationRequired, result.FailureKind);
        Assert.Empty(applied);

        queue.DiscardAll();
        queue.Stage(Group("fresh", Change("rescanned")));
        Assert.True((await queue.ApplyAllAsync(Recording(applied), _ => Ok())).IsSuccess);
        Assert.Equal(["rescanned"], applied);

        async Task ReplaceAfterReleaseAsync()
        {
            await releaseCallback.Task;
            queue.Unstage("g");
            queue.Stage(Group("replacement", Change("stale")));
        }
    }
    // ---- identity -----------------------------------------------------------

    [Fact]
    public void Stage_refuses_the_same_instance_twice()
    {
        var queue = Queue();
        var group = Group("g", Change("a"));
        queue.Stage(group);

        var ex = Assert.Throws<InvalidOperationException>(() => queue.Stage(group));

        Assert.Contains("'g'", ex.Message);
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public void Stage_refuses_a_different_instance_under_a_staged_id()
    {
        var queue = Queue();
        queue.Stage(Group("g", Change("a")));
        var events = 0;
        queue.PropertyChanged += (_, _) => events++;

        Assert.Throws<InvalidOperationException>(() => queue.Stage(Group("g", Change("b"))));

        Assert.Equal(1, queue.PendingCount);
        Assert.Equal("a", queue.PendingGroups[0].Changes[0].SettingId);
        Assert.Equal(0, events);
    }

    [Fact]
    public void Stage_of_a_single_change_never_collides_and_an_unstaged_id_is_free_again()
    {
        var queue = Queue();
        queue.Stage(Change("a"));
        queue.Stage(Change("a"));
        Assert.Equal(2, queue.PendingCount);

        queue.Stage(Group("g", Change("x")));
        queue.Unstage("g");
        queue.Stage(Group("g", Change("y")));

        Assert.Equal(3, queue.PendingCount);
        Assert.Equal("y", queue.PendingGroups[2].Changes[0].SettingId);
    }

    [Fact]
    public async Task Replacement_staged_under_a_completed_groups_id_mid_batch_stays_queued()
    {
        var queue = Queue();
        var original = Group("g", Change("a"), Change("b"));
        queue.Stage(original);
        ChangeGroup? replacement = null;
        var applied = new List<string>();

        var result = await queue.ApplyAllAsync(
            c =>
            {
                applied.Add(c.SettingId);
                if (c.SettingId == "a")
                {
                    // The person removes the group and sets it again while it applies.
                    queue.Unstage("g");
                    replacement = Group("g", Change("fresh"));
                    queue.Stage(replacement);
                }
                return Ok();
            },
            _ => Ok());

        Assert.True(result.IsSuccess);
        Assert.Equal(["a", "b"], applied);
        Assert.Equal(["a", "b"], result.Applied.Select(c => c.SettingId));
        var queued = Assert.Single(queue.PendingGroups);
        Assert.Same(replacement, queued);
        Assert.Equal("fresh", queued.Changes[0].SettingId);
    }

    [Fact]
    public async Task Replacement_staged_under_an_uncertain_groups_id_mid_batch_is_neither_marked_nor_removed()
    {
        var queue = Queue();
        queue.Stage(Group("g1", Change("done")));
        queue.Stage(Group("g2", Change("a"), Change("boom")));
        ChangeGroup? replacement = null;

        var result = await queue.ApplyAllAsync(
            c =>
            {
                if (c.SettingId != "boom")
                    return Ok();
                queue.Unstage("g2");
                replacement = Group("g2", Change("fresh"));
                queue.Stage(replacement);
                throw new InvalidOperationException();
            },
            _ => Ok());

        Assert.Equal(MutationFailureKind.ChangeThrew, result.FailureKind);
        Assert.Equal(["boom"], result.Uncertain.Select(c => c.SettingId));
        Assert.Same(replacement, Assert.Single(queue.PendingGroups));
        Assert.Empty(queue.ReconciliationRequired);
        Assert.Equal(["done"], result.Applied.Select(c => c.SettingId));
    }

    [Fact]
    public async Task Group_unstaged_while_its_apply_throws_is_not_marked()
    {
        var queue = Queue();
        queue.Stage(Group("g", Change("boom")));

        var result = await queue.ApplyAllAsync(
            _ =>
            {
                queue.Unstage("g");
                throw new InvalidOperationException();
            },
            _ => Ok());

        Assert.Equal(MutationFailureKind.ChangeThrew, result.FailureKind);
        Assert.Equal(["boom"], result.Uncertain.Select(c => c.SettingId));
        Assert.Equal(0, queue.PendingCount);
        Assert.Empty(queue.ReconciliationRequired);
    }

    [Fact]
    public async Task Marks_on_one_queue_do_not_block_another()
    {
        var executor = new ReversibleChangeExecutor();
        var interactive = PendingChangesService.Create(executor);
        var background = PendingChangesService.Create(executor);
        interactive.Stage(Group("ui", Change("ui-a")));
        background.Stage(Group("bg", Change("boom")));

        await background.ApplyAllAsync(_ => throw new InvalidOperationException(), _ => Ok());
        var applied = new List<string>();
        var result = await interactive.ApplyAllAsync(Recording(applied), _ => Ok());

        Assert.Single(background.ReconciliationRequired);
        Assert.Empty(interactive.ReconciliationRequired);
        Assert.True(result.IsSuccess);
        Assert.Equal(["ui-a"], applied);
    }

    [Fact]
    public async Task Group_staged_during_a_cancelled_batch_survives()
    {
        var queue = Queue();
        queue.Stage(Group("g1", Change("a"), Change("b")));
        using var cts = new CancellationTokenSource();

        var result = await queue.ApplyAllAsync(
            _ =>
            {
                queue.Stage(Group("late", Change("z")));
                cts.Cancel();
                return Ok();
            },
            _ => Ok(),
            cts.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(["a"], result.RolledBack.Select(c => c.SettingId));
        Assert.Equal(["g1", "late"], queue.PendingGroups.Select(g => g.GroupId));
    }

    [Fact]
    public async Task Cancelling_one_queue_never_touches_another_on_the_same_executor()
    {
        var executor = new ReversibleChangeExecutor();
        var interactive = PendingChangesService.Create(executor);
        var background = PendingChangesService.Create(executor);
        interactive.Stage(Group("ui", Change("ui-a")));
        background.Stage(Group("bg", Change("bg-a"), Change("bg-b")));
        using var cts = new CancellationTokenSource();
        var backgroundReverted = new List<string>();
        var interactiveEvents = 0;
        interactive.PropertyChanged += (_, _) => interactiveEvents++;

        var backgroundResult = await background.ApplyAllAsync(
            c =>
            {
                cts.Cancel();
                return Ok();
            },
            Recording(backgroundReverted),
            cts.Token);

        Assert.True(backgroundResult.WasCancelled);
        Assert.Equal(["bg-a"], backgroundReverted);
        Assert.Equal(1, background.PendingCount);
        Assert.Equal(1, interactive.PendingCount);
        Assert.False(interactive.IsApplying);
        Assert.Equal(0, interactiveEvents);

        var interactiveApplied = new List<string>();
        var interactiveResult = await interactive.ApplyAllAsync(Recording(interactiveApplied), _ => Ok(), cts.Token);

        // The already-cancelled token stops the other queue before any write, too.
        Assert.True(interactiveResult.WasCancelled);
        Assert.Empty(interactiveApplied);
        Assert.Equal(1, interactive.PendingCount);
    }

    [Fact]
    public async Task Second_batch_while_one_is_applying_is_rejected_so_nothing_replays()
    {
        var queue = Queue();
        queue.Stage(Group("g", Change("a")));
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource<OperationResult<bool>>();
        var applied = new List<string>();

        var first = queue.ApplyAllAsync(
            c =>
            {
                applied.Add(c.SettingId);
                started.SetResult();
                return release.Task;
            },
            _ => Ok());
        await started.Task;

        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.ApplyAllAsync(Recording(applied), _ => Ok()));

        release.SetResult(OperationResult<bool>.Success(true));
        var result = await first;

        Assert.True(result.IsSuccess);
        Assert.Equal(["a"], applied);
        Assert.False(queue.IsApplying);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task IsApplying_resets_after_throw_cancel_and_failure_paths()
    {
        foreach (var scenario in new[] { "throw", "cancel", "fail" })
        {
            var queue = Queue();
            queue.Stage(Group("g", Change("a"), Change("b")));
            using var cts = new CancellationTokenSource();

            var result = await queue.ApplyAllAsync(
                _ => scenario switch
                {
                    "throw" => throw new InvalidOperationException(),
                    "fail" => Task.FromResult(OperationResult<bool>.Failure("no", ErrorCategory.AccessDenied)),
                    _ => Cancelled(cts),
                },
                _ => Ok(),
                cts.Token);

            Assert.False(result.IsSuccess);
            Assert.False(queue.IsApplying);
        }

        static Task<OperationResult<bool>> Cancelled(CancellationTokenSource cts)
        {
            cts.Cancel();
            return Ok();
        }
    }

    // ---- default interface member -----------------------------------------

    [Fact]
    public async Task Default_token_overload_forwards_an_uncancellable_token_and_refuses_a_cancellable_one()
    {
        var fake = new MinimalQueue();
        using var cts = new CancellationTokenSource();

        var forwarded = await ((IPendingChangesService)fake).ApplyAllAsync(_ => Ok(), _ => Ok(), CancellationToken.None);
        Assert.True(forwarded.IsSuccess);
        Assert.Equal(1, fake.TwoArgumentCalls);
        Assert.Empty(fake.DefaultReconciliation);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            ((IPendingChangesService)fake).ApplyAllAsync(_ => Ok(), _ => Ok(), cts.Token));
        Assert.Equal(1, fake.TwoArgumentCalls);
    }

    /// <summary>An implementation that predates the token overload, as the integration fake does.</summary>
    private sealed class MinimalQueue : IPendingChangesService
    {
        public int TwoArgumentCalls { get; private set; }
        public int PendingCount => 0;
        public IReadOnlyList<ChangeGroup> PendingGroups => [];
        public bool IsApplying => false;
        public IReadOnlyList<GroupReconciliation> DefaultReconciliation => ((IPendingChangesService)this).ReconciliationRequired;
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public void Stage(ChangeDescriptor change) { }
        public void Stage(ChangeGroup group) { }
        public void Unstage(string groupId) { }
        public void DiscardAll() { }

        public Task<MutationResult> ApplyAllAsync(
            Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc,
            Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc)
        {
            TwoArgumentCalls++;
            return Task.FromResult(new MutationResult { IsSuccess = true, Applied = [], RolledBack = [] });
        }
    }
}
