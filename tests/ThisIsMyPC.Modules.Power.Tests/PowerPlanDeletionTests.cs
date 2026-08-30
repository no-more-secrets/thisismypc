using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Modules.Power.Actions;
using ThisIsMyPC.Modules.Power.Models;
using ThisIsMyPC.Modules.Power.Tests.Fakes;

namespace ThisIsMyPC.Modules.Power.Tests;

/// <summary>
/// Plan deletion is a one-way action: any plan except the active one may be
/// deleted (debloating vendor plan zoos is the point), already gone counts as
/// done, and nothing here touches the reversible pipeline.
/// </summary>
public sealed class PowerPlanDeletionTests
{
    private static readonly Guid BalancedGuid = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    private readonly FakePowerService _power = new();
    private readonly FakeRegistryService _registry = new();

    private PowerModule Module => new(_power, _registry);

    private static PowerPlan SamplePlan(Guid guid, string name) => new()
    {
        PlanGuid = guid,
        Name = name,
        IsActive = false,
    };

    [Fact]
    public async Task DeleteAction_RemovesAForeignPlan()
    {
        var foreign = Guid.NewGuid();
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        _power.AddPlan(foreign, "ChrisTitus - Ultimate Power Plan");

        var result = await Module.ExecuteActionAsync(
            PowerActionFactory.CreateDeletePlan(SamplePlan(foreign, "ChrisTitus - Ultimate Power Plan")));

        Assert.True(result.IsSuccess);
        Assert.Contains($"DeleteScheme:{foreign:D}", _power.Calls);
        Assert.Null(_power.GetPlan(foreign));
    }

    [Fact]
    public async Task DeleteAction_RefusesTheActivePlan()
    {
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);

        var result = await Module.ExecuteActionAsync(
            PowerActionFactory.CreateDeletePlan(SamplePlan(BalancedGuid, "Balanced")));

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(_power.Calls, c => c.StartsWith("DeleteScheme:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteAction_AlreadyGoneCountsAsDone()
    {
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);

        var result = await Module.ExecuteActionAsync(
            PowerActionFactory.CreateDeletePlan(SamplePlan(Guid.NewGuid(), "Long gone")));

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(_power.Calls, c => c.StartsWith("DeleteScheme:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteAction_FailureSurfaces()
    {
        var foreign = Guid.NewGuid();
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        _power.AddPlan(foreign, "Vendor Plan");
        _power.InjectFailure("DeleteScheme", ErrorCategory.AccessDenied);

        var result = await Module.ExecuteActionAsync(
            PowerActionFactory.CreateDeletePlan(SamplePlan(foreign, "Vendor Plan")));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task UnknownActionIdFails()
    {
        var result = await Module.ExecuteActionAsync(new ActionDescriptor
        {
            ModuleId = "Power Plans",
            ActionId = "frobnicate:everything",
            DisplayName = "Frobnicate",
            Detail = "n/a",
        });

        Assert.False(result.IsSuccess);
    }
}
