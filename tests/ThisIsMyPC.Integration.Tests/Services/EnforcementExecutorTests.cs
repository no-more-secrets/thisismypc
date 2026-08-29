using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Integration.Tests.Fakes;

namespace ThisIsMyPC.Integration.Tests.Services;

/// <summary>Pure unit tests against fakes — no trait, so they run in the CI filter.</summary>
public sealed class EnforcementExecutorTests
{
    private readonly FakeServiceControlService _services = new();
    private readonly FakeScheduledTaskService _tasks = new();

    private EnforcementExecutor CreateSut() => new(_services);

    private EnforcementExecutor CreateSutWithTasks() => new(_services, _tasks);

    private static ChangeDescriptor CreateChange(SettingEnforcement? enforcement) => new()
    {
        ModuleId = "Test",
        SettingId = "test-setting",
        DisplayName = "Test setting",
        SystemLocation = @"HKCU\Software\Test\Value",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Off",
        AfterDisplay = "On",
        ValueType = ChangeValueType.Registry_DWord,
        Enforcement = enforcement,
    };

    private static Func<ChangeDescriptor, Task<OperationResult<bool>>> Primary(
        List<ChangeDescriptor> received, bool succeed = true)
        => c =>
        {
            received.Add(c);
            return Task.FromResult(succeed
                ? OperationResult<bool>.Success(true)
                : OperationResult<bool>.Failure("primary failed", ErrorCategory.AccessDenied));
        };

    [Fact]
    public async Task Execute_CompanionServices_DisabledInOrderBeforePrimary()
    {
        _services.AddService("SvcA", ServiceState.Running, ServiceStartType.Automatic);
        _services.AddService("SvcB", ServiceState.Stopped, ServiceStartType.Manual);
        var change = CreateChange(new SettingEnforcement { CompanionServices = ["SvcA", "SvcB"] });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSut().ExecuteAsync(change, Primary(received));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Same(change, Assert.Single(received));
        Assert.Equal(
            [EnforcementStepType.DisableService, EnforcementStepType.DisableService, EnforcementStepType.PrimaryMutation],
            result.Steps.Select(s => s.StepType));
        Assert.Equal(["SvcA", "SvcB"], result.Steps.Take(2).Select(s => s.Target));
        // Running service was stopped then disabled; already-stopped service only disabled.
        Assert.Contains("Stop:SvcA", _services.Calls);
        Assert.DoesNotContain("Stop:SvcB", _services.Calls);
        Assert.Equal(ServiceStartType.Disabled, _services.GetService("SvcA")!.StartType);
        Assert.Equal(ServiceStartType.Disabled, _services.GetService("SvcB")!.StartType);
    }

    [Fact]
    public async Task Execute_PrimaryFails_CompanionsRestoredInReverseOrder()
    {
        _services.AddService("SvcA", ServiceState.Running, ServiceStartType.Automatic);
        _services.AddService("SvcB", ServiceState.Stopped, ServiceStartType.Manual);
        var change = CreateChange(new SettingEnforcement { CompanionServices = ["SvcA", "SvcB"] });

        var result = await CreateSut().ExecuteAsync(change, Primary([], succeed: false));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
        // Restored to captured before-state: SvcA Automatic + restarted, SvcB Manual, not started.
        Assert.Equal(ServiceStartType.Automatic, _services.GetService("SvcA")!.StartType);
        Assert.Equal(ServiceState.Running, _services.GetService("SvcA")!.State);
        Assert.Equal(ServiceStartType.Manual, _services.GetService("SvcB")!.StartType);
        Assert.Equal(ServiceState.Stopped, _services.GetService("SvcB")!.State);
        // Reverse order: SvcB restored before SvcA.
        var restoreCalls = _services.Calls.Where(c =>
            c is "SetStartType:SvcB:Manual" or "SetStartType:SvcA:Automatic").ToList();
        Assert.Equal(["SetStartType:SvcB:Manual", "SetStartType:SvcA:Automatic"], restoreCalls);
        // Disable steps for both companions are marked rolled back.
        Assert.All(
            result.Steps.Where(s => s.StepType == EnforcementStepType.DisableService && s.IsSuccess),
            s => Assert.True(s.WasRolledBack));
    }

    [Fact]
    public async Task Execute_SecondCompanionFails_FirstCompanionRestored()
    {
        _services.AddService("SvcA", ServiceState.Running, ServiceStartType.Automatic);
        _services.AddService("SvcB", ServiceState.Running, ServiceStartType.Automatic);
        _services.InjectFailure("Stop", "SvcB", ErrorCategory.AccessDenied);
        var change = CreateChange(new SettingEnforcement { CompanionServices = ["SvcA", "SvcB"] });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSut().ExecuteAsync(change, Primary(received));

        Assert.False(result.IsSuccess);
        Assert.Empty(received); // primary never ran
        Assert.Equal(ServiceStartType.Automatic, _services.GetService("SvcA")!.StartType);
        Assert.Equal(ServiceState.Running, _services.GetService("SvcA")!.State);
        var svcASteps = result.Steps.Where(s => s.Target == "SvcA").ToList();
        Assert.True(Assert.Single(svcASteps).WasRolledBack);
    }

    [Fact]
    public async Task Execute_OwnerModeRequired_FailsBeforeAnyMutation()
    {
        var change = CreateChange(new SettingEnforcement
        {
            OwnerModeRequired = true,
            CompanionServices = ["SvcA"],
        });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSut().ExecuteAsync(change, Primary(received));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.OwnerModeRequired, result.ErrorCategory);
        Assert.Empty(received);
        Assert.Empty(_services.Calls);
    }

    [Fact]
    public async Task Execute_SkuRestriction_IsInformational_NeverGates()
    {
        // Architecture FR129: SkuRestriction marks the setting cosmetic on that edition;
        // the UI informs, the apply proceeds (8-4 removed the 26-7 gate).
        var change = CreateChange(new SettingEnforcement { SkuRestriction = WindowsSku.Home });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSut().ExecuteAsync(change, Primary(received));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Single(received);
    }

    [Fact]
    public async Task Execute_CompanionTasks_DisabledBeforePrimary()
    {
        _tasks.AddTask(@"\Microsoft\Windows\Test\CompanionTask", enabled: true);
        var change = CreateChange(new SettingEnforcement { CompanionTasks = [@"\Microsoft\Windows\Test\CompanionTask"] });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSutWithTasks().ExecuteAsync(change, Primary(received));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Same(change, Assert.Single(received));
        Assert.Equal(
            [EnforcementStepType.DisableScheduledTask, EnforcementStepType.PrimaryMutation],
            result.Steps.Select(s => s.StepType));
        Assert.False(_tasks.GetTask(@"\Microsoft\Windows\Test\CompanionTask")!.IsEnabled);
    }

    [Fact]
    public async Task Execute_PrimaryFails_CompanionTaskReenabled()
    {
        _tasks.AddTask(@"\Microsoft\Windows\Test\CompanionTask", enabled: true);
        var change = CreateChange(new SettingEnforcement { CompanionTasks = [@"\Microsoft\Windows\Test\CompanionTask"] });

        var result = await CreateSutWithTasks().ExecuteAsync(change, Primary([], succeed: false));

        Assert.False(result.IsSuccess);
        Assert.True(_tasks.GetTask(@"\Microsoft\Windows\Test\CompanionTask")!.IsEnabled);
        Assert.True(result.Steps.Single(s => s.StepType == EnforcementStepType.DisableScheduledTask).WasRolledBack);
    }

    [Fact]
    public async Task Execute_AlreadyDisabledCompanionTask_NotReenabledOnRollback()
    {
        _tasks.AddTask(@"\Microsoft\Windows\Test\CompanionTask", enabled: false);
        var change = CreateChange(new SettingEnforcement { CompanionTasks = [@"\Microsoft\Windows\Test\CompanionTask"] });

        var result = await CreateSutWithTasks().ExecuteAsync(change, Primary([], succeed: false));

        Assert.False(result.IsSuccess);
        Assert.False(_tasks.GetTask(@"\Microsoft\Windows\Test\CompanionTask")!.IsEnabled);
    }

    [Fact]
    public async Task Revert_CompanionTask_Reenabled()
    {
        _tasks.AddTask(@"\Microsoft\Windows\Test\CompanionTask", enabled: false);
        var change = CreateChange(new SettingEnforcement { CompanionTasks = [@"\Microsoft\Windows\Test\CompanionTask"] });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSutWithTasks().RevertAsync(change, Primary(received));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(
            [EnforcementStepType.PrimaryMutation, EnforcementStepType.EnableScheduledTask],
            result.Steps.Select(s => s.StepType));
        Assert.True(_tasks.GetTask(@"\Microsoft\Windows\Test\CompanionTask")!.IsEnabled);
    }

    [Theory]
    [InlineData("tasks")]
    [InlineData("acl")]
    public async Task Execute_UnsupportedDimensions_FailUpFront(string dimension)
    {
        var enforcement = dimension switch
        {
            "tasks" => new SettingEnforcement { CompanionTasks = [@"\Microsoft\Windows\Test"] },
            _ => new SettingEnforcement { AclElevation = true },
        };
        var received = new List<ChangeDescriptor>();

        var result = await CreateSut().ExecuteAsync(CreateChange(enforcement), Primary(received));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.ServiceUnavailable, result.ErrorCategory);
        Assert.Contains("not yet supported", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(received);
    }

    [Fact]
    public async Task Execute_GPCacheWithoutRegistryService_GatesUpFront()
    {
        var change = CreateChange(new SettingEnforcement { GPCacheEntries = [GPCachePath] });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSut().ExecuteAsync(change, Primary(received));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.ServiceUnavailable, result.ErrorCategory);
        Assert.Contains("not available in this host", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(received);
    }

    [Fact]
    public async Task Execute_NullEnforcement_RunsPrimaryOnly()
    {
        var change = CreateChange(enforcement: null);
        var received = new List<ChangeDescriptor>();

        var result = await CreateSut().ExecuteAsync(change, Primary(received));

        Assert.True(result.IsSuccess);
        Assert.Single(received);
        Assert.Equal(EnforcementStepType.PrimaryMutation, Assert.Single(result.Steps).StepType);
        Assert.Empty(_services.Calls);
    }

    [Fact]
    public async Task Execute_ReversionVectorsOnly_JustRunsPrimary()
    {
        // Informational-only metadata must not touch any service.
        var change = CreateChange(new SettingEnforcement { ReversionVectors = ["Windows Update"] });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSut().ExecuteAsync(change, Primary(received));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Single(received);
        Assert.Empty(_services.Calls);
    }

    [Fact]
    public async Task Revert_PrimaryFirst_ThenDisabledCompanionsRestoredToManual()
    {
        _services.AddService("SvcA", ServiceState.Stopped, ServiceStartType.Disabled);
        _services.AddService("SvcB", ServiceState.Running, ServiceStartType.Automatic);
        var change = CreateChange(new SettingEnforcement { CompanionServices = ["SvcA", "SvcB"] });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSut().RevertAsync(change, Primary(received));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Single(received);
        Assert.Equal(EnforcementStepType.PrimaryMutation, result.Steps[0].StepType);
        // Disabled companion becomes Manual (startable, not forced on); non-disabled untouched.
        Assert.Equal(ServiceStartType.Manual, _services.GetService("SvcA")!.StartType);
        Assert.Equal(ServiceState.Stopped, _services.GetService("SvcA")!.State);
        Assert.Equal(ServiceStartType.Automatic, _services.GetService("SvcB")!.StartType);
        Assert.DoesNotContain("SetStartType:SvcB:Manual", _services.Calls);
    }

    [Fact]
    public async Task Execute_PrimaryThrows_CompanionsRestored()
    {
        _services.AddService("SvcA", ServiceState.Running, ServiceStartType.Automatic);
        var change = CreateChange(new SettingEnforcement { CompanionServices = ["SvcA"] });

        var result = await CreateSut().ExecuteAsync(
            change, _ => throw new InvalidOperationException("boom"));

        Assert.False(result.IsSuccess);
        Assert.Contains("boom", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(ServiceStartType.Automatic, _services.GetService("SvcA")!.StartType);
        Assert.Equal(ServiceState.Running, _services.GetService("SvcA")!.State);
        Assert.True(result.Steps.Single(s => s.StepType == EnforcementStepType.DisableService).WasRolledBack);
    }

    [Fact]
    public async Task Execute_DisableFailsAfterStop_ServiceRestarted()
    {
        _services.AddService("SvcA", ServiceState.Running, ServiceStartType.Automatic);
        _services.InjectFailure("SetStartType", "SvcA", ErrorCategory.AccessDenied);
        var change = CreateChange(new SettingEnforcement { CompanionServices = ["SvcA"] });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSut().ExecuteAsync(change, Primary(received));

        Assert.False(result.IsSuccess);
        Assert.Empty(received);
        // The stop was undone before failing — the caller never inherits a stopped companion.
        Assert.Equal(ServiceState.Running, _services.GetService("SvcA")!.State);
    }

    [Fact]
    public async Task Execute_RollbackRestoreFails_StepNotMarkedRolledBack()
    {
        _services.AddService("SvcA", ServiceState.Running, ServiceStartType.Automatic);
        _services.AddService("SvcB", ServiceState.Running, ServiceStartType.Automatic);
        _services.InjectFailure("Stop", "SvcB");
        _services.InjectFailure("Start", "SvcA"); // SvcA's rollback restart will fail
        var change = CreateChange(new SettingEnforcement { CompanionServices = ["SvcA", "SvcB"] });

        var result = await CreateSut().ExecuteAsync(change, Primary([]));

        Assert.False(result.IsSuccess);
        var svcAStep = result.Steps.Single(s => s.Target == "SvcA");
        Assert.False(svcAStep.WasRolledBack); // restore failed — must not be reported as rolled back
    }

    [Fact]
    public async Task Revert_PrimaryFails_CompanionsUntouched()
    {
        _services.AddService("SvcA", ServiceState.Stopped, ServiceStartType.Disabled);
        var change = CreateChange(new SettingEnforcement { CompanionServices = ["SvcA"] });

        var result = await CreateSut().RevertAsync(change, Primary([], succeed: false));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceStartType.Disabled, _services.GetService("SvcA")!.StartType);
        Assert.Empty(_services.Calls);
    }

    // --- GPCache clearing (Story 26-8) ---

    private const string GPCachePath = @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\GPCache";

    private readonly FakeRegistryService _registry = new();

    private EnforcementExecutor CreateSutWithRegistry() => new(_services, _tasks, _registry);

    [Fact]
    public async Task Execute_GPCache_ClearedRecursively_AfterCompanions_BeforePrimary()
    {
        _services.AddService("UsoSvc", ServiceState.Running, ServiceStartType.Automatic);
        _registry.ExistingKeys.Add(GPCachePath);
        var change = CreateChange(new SettingEnforcement
        {
            CompanionServices = ["UsoSvc"],
            GPCacheEntries = [GPCachePath],
        });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSutWithRegistry().ExecuteAsync(change, Primary(received));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Single(received);
        Assert.Equal(
            [EnforcementStepType.DisableService, EnforcementStepType.ClearGPCache, EnforcementStepType.PrimaryMutation],
            result.Steps.Select(s => s.StepType));
        Assert.Equal((GPCachePath, true), Assert.Single(_registry.DeletedKeys));
    }

    [Fact]
    public async Task Execute_WindowsUpdateModuleGPCachePath_PassesTheRealPathGuard()
    {
        // Ties the Windows Update module's constant to the executor's actual
        // IsSafeGPCachePath gate — a drifted path fails here, not on a live machine.
        var change = CreateChange(new SettingEnforcement
        {
            GPCacheEntries = [ThisIsMyPC.Modules.WindowsUpdate.WindowsUpdateRegistryPaths.GPCacheKeyPath],
        });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSutWithRegistry().ExecuteAsync(change, Primary(received));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Single(received);
    }

    [Fact]
    public async Task Execute_GPCache_MissingKey_IsSuccessWithoutDelete()
    {
        var change = CreateChange(new SettingEnforcement { GPCacheEntries = [GPCachePath] });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSutWithRegistry().ExecuteAsync(change, Primary(received));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Single(received);
        Assert.Empty(_registry.DeletedKeys);
        var clearStep = result.Steps.Single(s => s.StepType == EnforcementStepType.ClearGPCache);
        Assert.True(clearStep.IsSuccess);
    }

    [Fact]
    public async Task Execute_GPCacheClearFails_CompanionsRolledBack_PrimaryNeverRuns()
    {
        _services.AddService("UsoSvc", ServiceState.Running, ServiceStartType.Automatic);
        _registry.ExistingKeys.Add(GPCachePath);
        _registry.FailDeleteForPath = GPCachePath;
        var change = CreateChange(new SettingEnforcement
        {
            CompanionServices = ["UsoSvc"],
            GPCacheEntries = [GPCachePath],
        });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSutWithRegistry().ExecuteAsync(change, Primary(received));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
        Assert.Empty(received); // primary never ran
        Assert.Equal(ServiceStartType.Automatic, _services.GetService("UsoSvc")!.StartType);
        Assert.Equal(ServiceState.Running, _services.GetService("UsoSvc")!.State);
        Assert.True(result.Steps.Single(s => s.StepType == EnforcementStepType.DisableService).WasRolledBack);
    }

    [Fact]
    public async Task Revert_GPCache_ClearedAgain_AfterSuccessfulPrimaryRevert()
    {
        _registry.ExistingKeys.Add(GPCachePath);
        var change = CreateChange(new SettingEnforcement { GPCacheEntries = [GPCachePath] });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSutWithRegistry().RevertAsync(change, Primary(received));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Single(received);
        Assert.Equal(
            [EnforcementStepType.PrimaryMutation, EnforcementStepType.ClearGPCache],
            result.Steps.Select(s => s.StepType));
        Assert.Equal((GPCachePath, true), Assert.Single(_registry.DeletedKeys));
    }

    [Fact]
    public async Task Revert_GPCacheClearFails_ResultFails()
    {
        _registry.ExistingKeys.Add(GPCachePath);
        _registry.FailDeleteForPath = GPCachePath;
        var change = CreateChange(new SettingEnforcement { GPCacheEntries = [GPCachePath] });

        var result = await CreateSutWithRegistry().RevertAsync(change, Primary([]));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"HKLM")]
    [InlineData(@"HKLM\SOFTWARE\GPCache")] // too shallow: hive + 2 segments
    [InlineData(@"HKLM\SOFTWARE\Microsoft\WindowsUpdate")] // no GPCache segment
    [InlineData(@"HKLM\SOFTWARE\Microsoft\NotGPCacheHere\Sub")] // segment must EQUAL GPCache
    [InlineData(@"Foo\Bar\GPCache\Baz")] // unknown hive
    [InlineData(@"HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\GPCache")] // PowerShell-style hive
    public async Task Execute_UnsafeGPCachePath_RejectedBeforeAnyStep(string path)
    {
        _services.AddService("UsoSvc", ServiceState.Running, ServiceStartType.Automatic);
        var change = CreateChange(new SettingEnforcement
        {
            CompanionServices = ["UsoSvc"],
            GPCacheEntries = [path],
        });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSutWithRegistry().ExecuteAsync(change, Primary(received));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.EnforcementBlocked, result.ErrorCategory);
        Assert.Contains("invalid GPCache entry", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(received);
        Assert.Empty(_services.Calls); // gate fires before any companion mutation
        Assert.Empty(_registry.DeletedKeys);
    }

    [Fact]
    public async Task Execute_ServicesTasksGPCachePrimary_FullStepOrder()
    {
        _services.AddService("UsoSvc", ServiceState.Running, ServiceStartType.Automatic);
        _tasks.AddTask(@"\Microsoft\Windows\WindowsUpdate\Refresh Group Policy Cache", enabled: true);
        _registry.ExistingKeys.Add(GPCachePath);
        var change = CreateChange(new SettingEnforcement
        {
            CompanionServices = ["UsoSvc"],
            CompanionTasks = [@"\Microsoft\Windows\WindowsUpdate\Refresh Group Policy Cache"],
            GPCacheEntries = [GPCachePath],
        });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSutWithRegistry().ExecuteAsync(change, Primary(received));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(
            [
                EnforcementStepType.DisableService,
                EnforcementStepType.DisableScheduledTask,
                EnforcementStepType.ClearGPCache,
                EnforcementStepType.PrimaryMutation,
            ],
            result.Steps.Select(s => s.StepType));
    }

    [Fact]
    public async Task Execute_GPCacheClearFails_DisabledTasksAlsoRolledBack()
    {
        _tasks.AddTask(@"\Microsoft\Windows\Test\CompanionTask", enabled: true);
        _registry.ExistingKeys.Add(GPCachePath);
        _registry.FailDeleteForPath = GPCachePath;
        var change = CreateChange(new SettingEnforcement
        {
            CompanionTasks = [@"\Microsoft\Windows\Test\CompanionTask"],
            GPCacheEntries = [GPCachePath],
        });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSutWithRegistry().ExecuteAsync(change, Primary(received));

        Assert.False(result.IsSuccess);
        Assert.Empty(received);
        Assert.True(_tasks.GetTask(@"\Microsoft\Windows\Test\CompanionTask")!.IsEnabled);
        Assert.True(result.Steps.Single(s => s.StepType == EnforcementStepType.DisableScheduledTask).WasRolledBack);
    }

    [Fact]
    public async Task Execute_GPCacheSegmentMatch_IsCaseInsensitive()
    {
        var path = @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\gpcache";
        _registry.ExistingKeys.Add(path);
        var change = CreateChange(new SettingEnforcement { GPCacheEntries = [path] });

        var result = await CreateSutWithRegistry().ExecuteAsync(change, Primary([]));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal((path, true), Assert.Single(_registry.DeletedKeys));
    }
}
