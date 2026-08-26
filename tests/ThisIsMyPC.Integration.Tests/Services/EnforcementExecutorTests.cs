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
    private sealed class FakeCapabilityDetector : ICapabilityDetector
    {
        public WindowsSku? Sku { get; set; }
        public string? SkuDetectionFailureReason => null;
        public bool IsSkuRestricted(WindowsSku? restriction)
            => restriction is not null && Sku is not null && Sku == restriction;
        public bool IsAvailable(SystemCapability capability) => true;
        public ModuleAvailability GetAvailability(SystemCapability capability) => new(true);
    }

    private readonly FakeServiceControlService _services = new();
    private readonly FakeCapabilityDetector _capabilities = new();

    private EnforcementExecutor CreateSut() => new(_services, _capabilities);

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
    public async Task Execute_SkuRestricted_FailsWithSkuCategory()
    {
        _capabilities.Sku = WindowsSku.Home;
        var change = CreateChange(new SettingEnforcement { SkuRestriction = WindowsSku.Home });

        var result = await CreateSut().ExecuteAsync(change, Primary([]));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.SkuRestricted, result.ErrorCategory);
    }

    [Fact]
    public async Task Execute_SkuRestrictionForOtherSku_Proceeds()
    {
        _capabilities.Sku = WindowsSku.Pro;
        var change = CreateChange(new SettingEnforcement { SkuRestriction = WindowsSku.Home });
        var received = new List<ChangeDescriptor>();

        var result = await CreateSut().ExecuteAsync(change, Primary(received));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Single(received);
    }

    [Theory]
    [InlineData("tasks")]
    [InlineData("gpcache")]
    [InlineData("acl")]
    public async Task Execute_UnsupportedDimensions_FailUpFront(string dimension)
    {
        var enforcement = dimension switch
        {
            "tasks" => new SettingEnforcement { CompanionTasks = [@"\Microsoft\Windows\Test"] },
            "gpcache" => new SettingEnforcement { GPCacheEntries = ["TestEntry"] },
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
}
