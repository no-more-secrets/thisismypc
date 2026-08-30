using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Services;

public class PendingActionsServiceTests
{
    private static ActionDescriptor CreateTestAction(string actionId = "install:Test.App") => new()
    {
        ModuleId = "Software",
        ActionId = actionId,
        DisplayName = $"Action {actionId}",
        Detail = $"winget: {actionId}",
        UndoHint = "Uninstall from the app catalog",
    };

    private static Func<ActionDescriptor, Task<OperationResult<bool>>> AlwaysSucceed =>
        _ => Task.FromResult(OperationResult<bool>.Success(true));

    [Fact]
    public void Stage_IncrementsPendingCount()
    {
        var service = new PendingActionsService();

        service.Stage(CreateTestAction());

        Assert.Equal(1, service.PendingCount);
    }

    [Fact]
    public void Stage_SameActionIdTwice_IsIdempotent()
    {
        var service = new PendingActionsService();

        service.Stage(CreateTestAction("a"));
        service.Stage(CreateTestAction("a"));

        Assert.Equal(1, service.PendingCount);
    }

    [Fact]
    public void Unstage_RemovesByActionId()
    {
        var service = new PendingActionsService();
        service.Stage(CreateTestAction("a"));
        service.Stage(CreateTestAction("b"));

        service.Unstage("a");

        Assert.Equal(1, service.PendingCount);
        Assert.Equal("b", service.PendingActions[0].ActionId);
    }

    [Fact]
    public void Unstage_UnknownId_DoesNothing()
    {
        var service = new PendingActionsService();
        service.Stage(CreateTestAction("a"));

        service.Unstage("missing");

        Assert.Equal(1, service.PendingCount);
    }

    [Fact]
    public void IsStaged_ReflectsQueueMembership()
    {
        var service = new PendingActionsService();
        service.Stage(CreateTestAction("a"));

        Assert.True(service.IsStaged("a"));
        Assert.False(service.IsStaged("b"));
    }

    [Fact]
    public void DiscardAll_EmptiesQueue()
    {
        var service = new PendingActionsService();
        service.Stage(CreateTestAction("a"));
        service.Stage(CreateTestAction("b"));

        service.DiscardAll();

        Assert.Equal(0, service.PendingCount);
    }

    [Fact]
    public void Stage_RaisesPropertyChangedForCountAndActions()
    {
        var service = new PendingActionsService();
        var raised = new List<string?>();
        service.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        service.Stage(CreateTestAction());

        Assert.Contains(nameof(service.PendingCount), raised);
        Assert.Contains(nameof(service.PendingActions), raised);
    }

    [Fact]
    public void Stage_DuplicateActionId_RaisesNoPropertyChanged()
    {
        var service = new PendingActionsService();
        service.Stage(CreateTestAction("a"));
        var raised = new List<string?>();
        service.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        service.Stage(CreateTestAction("a"));

        Assert.Empty(raised);
    }

    [Fact]
    public async Task ApplyAllAsync_AllSucceed_QueueEmptiesAndResultIsSuccess()
    {
        var service = new PendingActionsService();
        service.Stage(CreateTestAction("a"));
        service.Stage(CreateTestAction("b"));

        var result = await service.ApplyAllAsync(AlwaysSucceed);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Succeeded.Count);
        Assert.Empty(result.Failed);
        Assert.Equal(0, service.PendingCount);
    }

    [Fact]
    public async Task ApplyAllAsync_ContinuesPastFailure()
    {
        var service = new PendingActionsService();
        service.Stage(CreateTestAction("a"));
        service.Stage(CreateTestAction("b"));
        service.Stage(CreateTestAction("c"));

        var executed = new List<string>();
        var result = await service.ApplyAllAsync(action =>
        {
            executed.Add(action.ActionId);
            return Task.FromResult(action.ActionId == "b"
                ? OperationResult<bool>.Failure("boom", ErrorCategory.ServiceUnavailable)
                : OperationResult<bool>.Success(true));
        });

        Assert.Equal(["a", "b", "c"], executed);
        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Succeeded.Count);
        Assert.Single(result.Failed);
        Assert.Equal("b", result.Failed[0].Action.ActionId);
        Assert.Equal("boom", result.Failed[0].ErrorMessage);
    }

    [Fact]
    public async Task ApplyAllAsync_FailedActionStaysStaged()
    {
        var service = new PendingActionsService();
        service.Stage(CreateTestAction("a"));
        service.Stage(CreateTestAction("b"));

        await service.ApplyAllAsync(action =>
            Task.FromResult(action.ActionId == "a"
                ? OperationResult<bool>.Failure("boom", ErrorCategory.ServiceUnavailable)
                : OperationResult<bool>.Success(true)));

        Assert.Equal(1, service.PendingCount);
        Assert.Equal("a", service.PendingActions[0].ActionId);
    }

    [Fact]
    public async Task ApplyAllAsync_ExecutorThrows_BecomesFailureAndBatchContinues()
    {
        var service = new PendingActionsService();
        service.Stage(CreateTestAction("a"));
        service.Stage(CreateTestAction("b"));

        var result = await service.ApplyAllAsync(action =>
            action.ActionId == "a"
                ? throw new InvalidOperationException("crash")
                : Task.FromResult(OperationResult<bool>.Success(true)));

        Assert.Single(result.Failed);
        Assert.Equal("crash", result.Failed[0].ErrorMessage);
        Assert.Single(result.Succeeded);
    }

    [Fact]
    public async Task ApplyAllAsync_ActionStagedMidBatchSurvives()
    {
        var service = new PendingActionsService();
        service.Stage(CreateTestAction("a"));

        var result = await service.ApplyAllAsync(action =>
        {
            service.Stage(CreateTestAction("late"));
            return Task.FromResult(OperationResult<bool>.Success(true));
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, service.PendingCount);
        Assert.Equal("late", service.PendingActions[0].ActionId);
    }

    [Fact]
    public async Task ApplyAllAsync_SetsIsApplyingAndCurrentActionDuringRun()
    {
        var service = new PendingActionsService();
        service.Stage(CreateTestAction("a"));

        bool? applyingDuring = null;
        string? currentDuring = null;
        var result = await service.ApplyAllAsync(_ =>
        {
            applyingDuring = service.IsApplying;
            currentDuring = service.CurrentActionDisplay;
            return Task.FromResult(OperationResult<bool>.Success(true));
        });

        Assert.True(applyingDuring);
        Assert.Equal("Action a", currentDuring);
        Assert.False(service.IsApplying);
        Assert.Null(service.CurrentActionDisplay);
    }

    [Fact]
    public async Task ApplyAllAsync_ConcurrentSecondBatchIsRejected()
    {
        var service = new PendingActionsService();
        service.Stage(CreateTestAction("a"));

        var gate = new TaskCompletionSource();
        var firstBatch = service.ApplyAllAsync(async _ =>
        {
            await gate.Task;
            return OperationResult<bool>.Success(true);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAllAsync(AlwaysSucceed));

        gate.SetResult();
        var result = await firstBatch;
        Assert.True(result.IsSuccess);
        Assert.Single(result.Succeeded);
    }

    [Fact]
    public async Task ApplyAllAsync_EmptyQueue_ReturnsSuccessWithNoWork()
    {
        var service = new PendingActionsService();

        var result = await service.ApplyAllAsync(AlwaysSucceed);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Succeeded);
        Assert.Empty(result.Failed);
    }
}
