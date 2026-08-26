using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Tests.Fakes;

namespace ThisIsMyPC.Core.Tests.Services;

public sealed class PendingChangesEnforcementTests
{
    private static ChangeDescriptor CreateTestChange(
        string settingId = "test",
        SettingEnforcement? enforcement = null) => new()
    {
        ModuleId = "TestModule",
        SettingId = settingId,
        DisplayName = $"Change {settingId}",
        SystemLocation = "HKCU\\Test",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "0",
        AfterDisplay = "1",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Modify,
        Enforcement = enforcement
    };

    private static SettingEnforcement WithServices(params string[] services) =>
        new() { CompanionServices = services };

    private static ChangeGroup Group(string groupId, params ChangeDescriptor[] changes) => new()
    {
        GroupId = groupId,
        DisplayName = $"Group {groupId}",
        Description = $"Test group {groupId}",
        Changes = changes
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
    public async Task NullEnforcement_BypassesExecutor()
    {
        var executor = new FakeEnforcementExecutor();
        var service = new PendingChangesService(executor);
        service.Stage(CreateTestChange());
        var applied = new List<ChangeDescriptor>();

        var result = await service.ApplyAllAsync(Recording(applied), Recording([]));

        Assert.True(result.IsSuccess);
        Assert.Single(applied);
        Assert.Empty(executor.ExecutedChanges);
    }

    [Fact]
    public async Task NonNullEnforcement_DelegatesToExecutor_ApplyFuncNotCalledDirectly()
    {
        var executor = new FakeEnforcementExecutor();
        var service = new PendingChangesService(executor);
        service.Stage(CreateTestChange("enforced", WithServices("WbioSrvc")));
        var applied = new List<ChangeDescriptor>();

        var result = await service.ApplyAllAsync(Recording(applied), Recording([]));

        Assert.True(result.IsSuccess);
        Assert.Empty(applied);
        Assert.Single(executor.ExecutedChanges);
        Assert.Equal("enforced", executor.ExecutedChanges[0].SettingId);
        Assert.NotNull(executor.LastApplyDelegate);
    }

    [Fact]
    public async Task MixedBatch_RoutesEachChangeCorrectly()
    {
        var executor = new FakeEnforcementExecutor();
        var service = new PendingChangesService(executor);
        service.Stage(Group("g1",
            CreateTestChange("simple1"),
            CreateTestChange("enforced1", WithServices("WSearch")),
            CreateTestChange("simple2")));
        var applied = new List<ChangeDescriptor>();

        var result = await service.ApplyAllAsync(Recording(applied), Recording([]));

        Assert.True(result.IsSuccess);
        Assert.Equal(["simple1", "simple2"], applied.Select(c => c.SettingId));
        Assert.Equal(["enforced1"], executor.ExecutedChanges.Select(c => c.SettingId));
        Assert.Equal(3, result.Applied.Count);
    }

    [Fact]
    public async Task EnforcementFailure_RollsBackViaExecutorForEnforced_RevertFuncForSimple()
    {
        var executor = new FakeEnforcementExecutor();
        var service = new PendingChangesService(executor);
        var simple = CreateTestChange("simple");
        var enforcedOk = CreateTestChange("enforcedOk", WithServices("WSearch"));
        var failing = CreateTestChange("failing");
        service.Stage(Group("g1", simple, enforcedOk, failing));

        var reverted = new List<ChangeDescriptor>();
        var result = await service.ApplyAllAsync(
            Recording([], c => c.SettingId != "failing"),
            Recording(reverted));

        Assert.False(result.IsSuccess);
        Assert.Equal("failing", result.Failed?.SettingId);
        // enforcedOk rolled back through the executor, simple through revertFunc
        Assert.Equal(["enforcedOk"], executor.RevertedChanges.Select(c => c.SettingId));
        Assert.Equal(["simple"], reverted.Select(c => c.SettingId));
        Assert.Equal(2, result.RolledBack.Count);
    }

    [Fact]
    public async Task ExecutorReportsFailure_TreatedAsFailedChange()
    {
        var executor = new FakeEnforcementExecutor
        {
            NextExecuteResult = new EnforcementResult
            {
                IsSuccess = false,
                ErrorMessage = "companion service refused to stop",
                ErrorCategory = ErrorCategory.ServiceUnavailable
            }
        };
        var service = new PendingChangesService(executor);
        service.Stage(CreateTestChange("enforced", WithServices("WbioSrvc")));

        var result = await service.ApplyAllAsync(Recording([]), Recording([]));

        Assert.False(result.IsSuccess);
        Assert.Equal("companion service refused to stop", result.ErrorMessage);
        Assert.Equal(ErrorCategory.ServiceUnavailable, result.ErrorCategory);
    }

    [Fact]
    public async Task EnforcedChange_WithoutExecutor_ThrowsInvalidOperation()
    {
        var service = new PendingChangesService();
        service.Stage(CreateTestChange("enforced", WithServices("WbioSrvc")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAllAsync(Recording([]), Recording([])));
    }

    [Fact]
    public async Task EnforcedChange_WithoutExecutor_ThrowsBeforeApplyingAnything()
    {
        var service = new PendingChangesService();
        service.Stage(CreateTestChange("simple"));
        service.Stage(CreateTestChange("enforced", WithServices("WbioSrvc")));
        var applied = new List<ChangeDescriptor>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAllAsync(Recording(applied), Recording([])));

        Assert.Empty(applied);
        Assert.False(service.IsApplying);
        Assert.Equal(2, service.PendingCount);
    }

    [Fact]
    public async Task Executor_ReceivedDelegate_RoutesToApplyFunc()
    {
        var executor = new FakeEnforcementExecutor { InvokePrimary = true };
        var service = new PendingChangesService(executor);
        service.Stage(CreateTestChange("enforced", WithServices("WbioSrvc")));
        var applied = new List<ChangeDescriptor>();

        var result = await service.ApplyAllAsync(Recording(applied), Recording([]));

        Assert.True(result.IsSuccess);
        // The executor invoked the delegate it was handed, and that delegate is the
        // caller's applyFunc — the primary mutation genuinely routes through it.
        Assert.Equal(["enforced"], applied.Select(c => c.SettingId));
    }

    [Fact]
    public async Task StagingDuringApply_SurvivesSuccessfulBatch()
    {
        var service = new PendingChangesService();
        service.Stage(CreateTestChange("a"));
        var stagedDuringApply = false;

        var result = await service.ApplyAllAsync(
            c =>
            {
                if (!stagedDuringApply)
                {
                    stagedDuringApply = true;
                    service.Stage(CreateTestChange("late"));
                }
                return Task.FromResult(OperationResult<bool>.Success(true));
            },
            Recording([]));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, service.PendingCount);
        Assert.Equal("Change late", service.PendingGroups[0].DisplayName);
    }

    [Fact]
    public async Task UnstageDuringApply_FailurePath_DoesNotRemoveWrongGroup()
    {
        var service = new PendingChangesService();
        var g0 = Group("g0", CreateTestChange("first"));
        var g1 = Group("g1", CreateTestChange("failing"));
        service.Stage(g0);
        service.Stage(g1);

        var result = await service.ApplyAllAsync(
            c =>
            {
                if (c.SettingId == "failing")
                {
                    // UI removes the already-applied g0 while g1 is mid-apply; the
                    // failure cleanup must not remove g1 by stale index.
                    service.Unstage("g0");
                    return Task.FromResult(OperationResult<bool>.Failure("boom", ErrorCategory.AccessDenied));
                }
                return Task.FromResult(OperationResult<bool>.Success(true));
            },
            Recording([]));

        Assert.False(result.IsSuccess);
        // g1 (the failed group) must still be pending; only g0 was applied+removed.
        Assert.Equal(1, service.PendingCount);
        Assert.Equal("g1", service.PendingGroups[0].GroupId);
    }

    [Fact]
    public async Task NullExecutor_SimpleChanges_BackwardCompatible()
    {
        var service = new PendingChangesService();
        service.Stage(CreateTestChange("a"));
        service.Stage(CreateTestChange("b"));
        var applied = new List<ChangeDescriptor>();

        var result = await service.ApplyAllAsync(Recording(applied), Recording([]));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, applied.Count);
    }
}
