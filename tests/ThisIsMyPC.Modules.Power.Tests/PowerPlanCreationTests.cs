using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Modules.Power.Changes;
using ThisIsMyPC.Modules.Power.Models;
using ThisIsMyPC.Modules.Power.Tests.Fakes;

namespace ThisIsMyPC.Modules.Power.Tests;

/// <summary>
/// Creating a plan is a reversible change: apply duplicates the source and
/// names the copy with the marker description; undo deletes that copy and
/// nothing else.
/// </summary>
public sealed class PowerPlanCreationTests
{
    private static readonly Guid BalancedGuid = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly PowerPlan Balanced = new() { PlanGuid = BalancedGuid, Name = "Balanced", IsActive = true };

    private readonly FakePowerService _power = new();
    private readonly FakeRegistryService _registry = new();

    private PowerModule Module => new(_power, _registry);

    [Fact]
    public void Factory_NamesTheChangeAfterThePlanAndPointsAtTheSource()
    {
        var change = PowerPlanChangeFactory.CreatePlanChange("  Gaming ", Balanced);

        Assert.Equal("create-plan:Gaming", change.SettingId);
        Assert.Equal("Create power plan Gaming", change.DisplayName);
        Assert.Equal("Copy of Balanced", change.AfterDisplay);
        Assert.Equal(ChangeCategory.Create, change.Category);
        Assert.Equal("0", change.BeforeValue);
        Assert.Equal("1", change.AfterValue);
        Assert.True(PowerPlanChangeFactory.TryParseSourceGuid(change.SystemLocation, out var source));
        Assert.Equal(BalancedGuid, source);
    }

    [Fact]
    public async Task Apply_DuplicatesTheSourceAndNamesTheCopy()
    {
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        var change = PowerPlanChangeFactory.CreatePlanChange("Gaming", Balanced);

        var result = await Module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains($"DuplicateScheme:{BalancedGuid:D}", _power.Calls);
        var created = _power.EnumeratePlans().Value!.Single(p => p.Name == "Gaming");
        Assert.Equal(PowerPlanChangeFactory.CreatedPlanMarker, created.Description);
        Assert.False(created.IsActive);
    }

    [Fact]
    public async Task Apply_Twice_MakesOneCopy()
    {
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        var change = PowerPlanChangeFactory.CreatePlanChange("Gaming", Balanced);

        await Module.ApplyChangeAsync(change);
        await Module.ApplyChangeAsync(change);

        Assert.Single(_power.EnumeratePlans().Value!, p => p.Name == "Gaming");
    }

    [Fact]
    public async Task Apply_NamingFails_RollsTheDuplicateBack()
    {
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        _power.InjectFailure("WriteSchemeText");
        var change = PowerPlanChangeFactory.CreatePlanChange("Gaming", Balanced);

        var result = await Module.ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
        Assert.Contains(_power.Calls, c => c.StartsWith("DeleteScheme:", StringComparison.Ordinal));
        Assert.Single(_power.EnumeratePlans().Value!);
    }

    [Fact]
    public async Task Revert_DeletesOnlyTheCopyThisAppMade()
    {
        var ours = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        _power.AddPlan(theirs, "Gaming", description: "Someone else's plan");
        _power.AddPlan(ours, "Gaming", description: PowerPlanChangeFactory.CreatedPlanMarker);
        var change = PowerPlanChangeFactory.CreatePlanChange("Gaming", Balanced);
        var revert = change with { BeforeValue = change.AfterValue, AfterValue = change.BeforeValue };

        var result = await Module.RevertChangeAsync(revert);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Null(_power.GetPlan(ours));
        Assert.NotNull(_power.GetPlan(theirs));
    }

    [Fact]
    public async Task Revert_WhenTheCopyIsGone_IsDone()
    {
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        var change = PowerPlanChangeFactory.CreatePlanChange("Gaming", Balanced);
        var revert = change with { BeforeValue = "1", AfterValue = "0" };

        var result = await Module.RevertChangeAsync(revert);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(_power.Calls, c => c.StartsWith("DeleteScheme:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Revert_RefusesWhileTheCopyIsActive()
    {
        _power.AddPlan(BalancedGuid, "Balanced");
        _power.AddPlan(Guid.NewGuid(), "Gaming", description: PowerPlanChangeFactory.CreatedPlanMarker, isActive: true);
        var change = PowerPlanChangeFactory.CreatePlanChange("Gaming", Balanced);
        var revert = change with { BeforeValue = "1", AfterValue = "0" };

        var result = await Module.RevertChangeAsync(revert);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.ServiceUnavailable, result.ErrorCategory);
        Assert.Contains("active plan", result.ErrorMessage, StringComparison.Ordinal);
    }
}
