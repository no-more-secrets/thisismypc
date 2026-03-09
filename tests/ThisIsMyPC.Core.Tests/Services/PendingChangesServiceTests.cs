using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Services;

public class PendingChangesServiceTests
{
    private static ChangeDescriptor CreateTestChange(string settingId = "test", string before = "0", string? after = "1") => new()
    {
        ModuleId = "TestModule",
        SettingId = settingId,
        DisplayName = $"Change {settingId}",
        SystemLocation = "HKCU\\Test",
        BeforeValue = before,
        AfterValue = after,
        BeforeDisplay = before,
        AfterDisplay = after,
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Modify
    };

    private static ChangeGroup CreateTestGroup(string groupId, params ChangeDescriptor[] changes) => new()
    {
        GroupId = groupId,
        DisplayName = $"Group {groupId}",
        Description = $"Test group {groupId}",
        Changes = changes
    };

    [Fact]
    public void Stage_SingleChangeDescriptor_IncrementsPendingCount()
    {
        var service = new PendingChangesService();

        service.Stage(CreateTestChange());

        Assert.Equal(1, service.PendingCount);
    }

    [Fact]
    public void Stage_ChangeGroup_IncrementsPendingCountByOne()
    {
        var service = new PendingChangesService();
        var group = CreateTestGroup("g1", CreateTestChange("a"), CreateTestChange("b"));

        service.Stage(group);

        Assert.Equal(1, service.PendingCount);
    }

    [Fact]
    public void Stage_RejectsWhenBeforeValueIsNull()
    {
        var service = new PendingChangesService();

        // ChangeDescriptor.BeforeValue is `required string` (non-nullable),
        // so we suppress the warning to test the runtime guard.
#pragma warning disable CS8625
        var change = new ChangeDescriptor
        {
            ModuleId = "TestModule",
            SettingId = "bad",
            DisplayName = "Bad Change",
            SystemLocation = "HKCU\\Test",
            BeforeValue = null!,
            AfterValue = "1",
            BeforeDisplay = "?",
            AfterDisplay = "1",
            ValueType = ChangeValueType.Registry_DWord,
            Category = ChangeCategory.Modify
        };
#pragma warning restore CS8625

        Assert.Throws<ArgumentException>(() => service.Stage(change));
        Assert.Equal(0, service.PendingCount);
    }

    [Fact]
    public async Task ApplyAll_CallsApplyForEachPendingChangeInOrder()
    {
        var service = new PendingChangesService();
        var applied = new List<string>();

        service.Stage(CreateTestChange("first"));
        service.Stage(CreateTestChange("second"));

        var result = await service.ApplyAllAsync(
            applyFunc: change =>
            {
                applied.Add(change.SettingId);
                return Task.FromResult(OperationResult<bool>.Success(true));
            },
            revertFunc: _ => Task.FromResult(OperationResult<bool>.Success(true)));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Applied.Count);
        Assert.Equal(["first", "second"], applied);
        Assert.Equal(0, service.PendingCount);
    }

    [Fact]
    public async Task ApplyAll_RollsBackInReverseOrderWhenChangeFails()
    {
        var service = new PendingChangesService();
        var rolledBackIds = new List<string>();

        var group = CreateTestGroup("g1",
            CreateTestChange("a"),
            CreateTestChange("b"),
            CreateTestChange("fail"));

        service.Stage(group);

        var result = await service.ApplyAllAsync(
            applyFunc: change =>
            {
                if (change.SettingId == "fail")
                    return Task.FromResult(OperationResult<bool>.Failure("boom", ErrorCategory.ServiceUnavailable));
                return Task.FromResult(OperationResult<bool>.Success(true));
            },
            revertFunc: change =>
            {
                rolledBackIds.Add(change.SettingId);
                return Task.FromResult(OperationResult<bool>.Success(true));
            });

        Assert.False(result.IsSuccess);
        Assert.Equal("fail", result.Failed!.SettingId);
        Assert.Equal(["b", "a"], rolledBackIds);
        Assert.Equal(2, result.RolledBack.Count);
    }

    [Fact]
    public async Task ApplyAll_ReturnsMutationResultWithAppliedFailedRolledBackLists()
    {
        var service = new PendingChangesService();

        // First group succeeds, second group fails on second change
        service.Stage(CreateTestGroup("g1", CreateTestChange("ok1")));
        service.Stage(CreateTestGroup("g2", CreateTestChange("ok2"), CreateTestChange("bad")));

        var result = await service.ApplyAllAsync(
            applyFunc: change =>
            {
                if (change.SettingId == "bad")
                    return Task.FromResult(OperationResult<bool>.Failure("error", ErrorCategory.AccessDenied));
                return Task.FromResult(OperationResult<bool>.Success(true));
            },
            revertFunc: _ => Task.FromResult(OperationResult<bool>.Success(true)));

        Assert.False(result.IsSuccess);
        Assert.Equal("ok1", result.Applied[0].SettingId);
        Assert.Equal("bad", result.Failed!.SettingId);
        Assert.Single(result.RolledBack);
        Assert.Equal("ok2", result.RolledBack[0].SettingId);
        Assert.Equal("error", result.ErrorMessage);
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
    }

    [Fact]
    public void DiscardAll_ClearsAllPendingChanges_PendingCountReturnsToZero()
    {
        var service = new PendingChangesService();

        service.Stage(CreateTestChange("a"));
        service.Stage(CreateTestChange("b"));
        Assert.Equal(2, service.PendingCount);

        service.DiscardAll();

        Assert.Equal(0, service.PendingCount);
        Assert.Empty(service.PendingGroups);
    }

    [Fact]
    public void Unstage_RemovesSpecificChange()
    {
        var service = new PendingChangesService();

        var group1 = CreateTestGroup("g1", CreateTestChange("a"));
        var group2 = CreateTestGroup("g2", CreateTestChange("b"));
        service.Stage(group1);
        service.Stage(group2);

        service.Unstage("g1");

        Assert.Equal(1, service.PendingCount);
        Assert.Equal("g2", service.PendingGroups[0].GroupId);
    }

    [Fact]
    public void Stage_RaisesPropertyChangedForPendingCountAndPendingGroups()
    {
        var service = new PendingChangesService();
        var changedProperties = new List<string>();
        service.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        service.Stage(CreateTestChange());

        Assert.Contains(nameof(service.PendingCount), changedProperties);
        Assert.Contains(nameof(service.PendingGroups), changedProperties);
    }

    [Fact]
    public void DiscardAll_RaisesPropertyChanged()
    {
        var service = new PendingChangesService();
        service.Stage(CreateTestChange());

        var changedProperties = new List<string>();
        service.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        service.DiscardAll();

        Assert.Contains(nameof(service.PendingCount), changedProperties);
        Assert.Contains(nameof(service.PendingGroups), changedProperties);
    }

    [Fact]
    public void Unstage_RaisesPropertyChanged()
    {
        var service = new PendingChangesService();
        var group = CreateTestGroup("g1", CreateTestChange("a"));
        service.Stage(group);

        var changedProperties = new List<string>();
        service.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        service.Unstage("g1");

        Assert.Contains(nameof(service.PendingCount), changedProperties);
        Assert.Contains(nameof(service.PendingGroups), changedProperties);
    }

    [Fact]
    public async Task ApplyAll_RemovesSuccessfulGroupsFromPendingOnPartialFailure()
    {
        var service = new PendingChangesService();

        service.Stage(CreateTestGroup("g1", CreateTestChange("ok1")));
        service.Stage(CreateTestGroup("g2", CreateTestChange("bad")));

        await service.ApplyAllAsync(
            applyFunc: change =>
            {
                if (change.SettingId == "bad")
                    return Task.FromResult(OperationResult<bool>.Failure("error", ErrorCategory.AccessDenied));
                return Task.FromResult(OperationResult<bool>.Success(true));
            },
            revertFunc: _ => Task.FromResult(OperationResult<bool>.Success(true)));

        // Only the failed group should remain pending
        Assert.Equal(1, service.PendingCount);
        Assert.Equal("g2", service.PendingGroups[0].GroupId);
    }

    [Fact]
    public async Task ApplyAll_AggregatesRequiredRestarts_FromAppliedChanges()
    {
        var service = new PendingChangesService();
        var explorerRestartChange = new ChangeDescriptor
        {
            ModuleId = "Explorer",
            SettingId = "classic-menu",
            DisplayName = "Classic menu",
            SystemLocation = @"HKCU\Test",
            BeforeValue = "0",
            AfterValue = "1",
            BeforeDisplay = "Off",
            AfterDisplay = "On",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Enable,
            RestartRequirement = RestartRequirement.ExplorerRestart,
        };

        service.Stage(CreateTestChange("no-restart")); // RestartRequirement.None (default)
        service.Stage(explorerRestartChange);

        var result = await service.ApplyAllAsync(
            applyFunc: _ => Task.FromResult(OperationResult<bool>.Success(true)),
            revertFunc: _ => Task.FromResult(OperationResult<bool>.Success(true)));

        Assert.True(result.IsSuccess);
        Assert.Single(result.RequiredRestarts);
        Assert.Contains(RestartRequirement.ExplorerRestart, result.RequiredRestarts);
    }

    [Fact]
    public async Task ApplyAll_RequiredRestarts_Empty_WhenNoRestartRequired()
    {
        var service = new PendingChangesService();
        service.Stage(CreateTestChange("s1"));
        service.Stage(CreateTestChange("s2"));

        var result = await service.ApplyAllAsync(
            applyFunc: _ => Task.FromResult(OperationResult<bool>.Success(true)),
            revertFunc: _ => Task.FromResult(OperationResult<bool>.Success(true)));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.RequiredRestarts);
    }

    [Fact]
    public async Task ApplyAll_RequiredRestarts_Deduplicates()
    {
        var service = new PendingChangesService();

        for (var i = 0; i < 3; i++)
        {
            service.Stage(new ChangeDescriptor
            {
                ModuleId = "Explorer",
                SettingId = $"ctx-handler-{i}",
                DisplayName = $"Handler {i}",
                SystemLocation = @"HKCU\Test",
                BeforeValue = "0",
                AfterValue = "1",
                BeforeDisplay = "Off",
                AfterDisplay = "On",
                ValueType = ChangeValueType.Registry_String,
                Category = ChangeCategory.Enable,
                RestartRequirement = RestartRequirement.ExplorerRestart,
            });
        }

        var result = await service.ApplyAllAsync(
            applyFunc: _ => Task.FromResult(OperationResult<bool>.Success(true)),
            revertFunc: _ => Task.FromResult(OperationResult<bool>.Success(true)));

        Assert.Single(result.RequiredRestarts);
    }

    [Fact]
    public async Task ApplyAll_RequiredRestarts_PopulatedOnPartialFailure()
    {
        var service = new PendingChangesService();

        // Group 1: succeeds with ExplorerRestart requirement
        service.Stage(new ChangeDescriptor
        {
            ModuleId = "Explorer",
            SettingId = "restart-setting",
            DisplayName = "Restart Setting",
            SystemLocation = @"HKCU\Test",
            BeforeValue = "0",
            AfterValue = "1",
            BeforeDisplay = "Off",
            AfterDisplay = "On",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Enable,
            RestartRequirement = RestartRequirement.ExplorerRestart,
        });

        // Group 2: fails
        service.Stage(CreateTestGroup("g2", CreateTestChange("bad")));

        var result = await service.ApplyAllAsync(
            applyFunc: change =>
            {
                if (change.SettingId == "bad")
                    return Task.FromResult(OperationResult<bool>.Failure("error", ErrorCategory.AccessDenied));
                return Task.FromResult(OperationResult<bool>.Success(true));
            },
            revertFunc: _ => Task.FromResult(OperationResult<bool>.Success(true)));

        Assert.False(result.IsSuccess);
        Assert.Single(result.RequiredRestarts);
        Assert.Contains(RestartRequirement.ExplorerRestart, result.RequiredRestarts);
    }
}
