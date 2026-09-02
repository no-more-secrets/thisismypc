using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Modules.Power.Changes;
using ThisIsMyPC.Modules.Power.Models;
using ThisIsMyPC.Modules.Power.Tests.Fakes;

namespace ThisIsMyPC.Modules.Power.Tests;

/// <summary>
/// A deleted stock plan (Balanced, High performance, Power saver) comes back
/// under its own GUID from Windows' defaults; undo deletes it again.
/// </summary>
public sealed class PowerStockPlanRestoreTests
{
    private readonly FakePowerService _power = new();
    private readonly FakeRegistryService _registry = new();

    private PowerModule Module => new(_power, _registry);

    [Fact]
    public void Factory_TargetsTheStockGuid()
    {
        var change = PowerPlanChangeFactory.CreateStockPlanRestore(StockPowerPlan.PowerSaver);

        Assert.Equal("add-stock-plan:a1841308-3541-4fab-bc81-f71556f20b4a", change.SettingId);
        Assert.Equal("Add power plan Power saver", change.DisplayName);
        Assert.Equal(ChangeCategory.Create, change.Category);
        Assert.Equal("Windows default", change.AfterDisplay);
    }

    [Fact]
    public async Task Apply_RecreatesThePlanUnderItsOwnGuid()
    {
        _power.AddPlan(StockPowerPlan.Balanced.PlanGuid, "Balanced", isActive: true);
        var change = PowerPlanChangeFactory.CreateStockPlanRestore(StockPowerPlan.HighPerformance);

        var result = await Module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var guid = StockPowerPlan.HighPerformance.PlanGuid;
        Assert.Contains($"RestoreDefaultScheme:{guid:D}", _power.Calls);
        Assert.Equal("High performance", _power.GetPlan(guid)?.Name);
    }

    [Fact]
    public async Task Apply_WhenThePlanIsThere_DoesNothing()
    {
        _power.AddPlan(StockPowerPlan.Balanced.PlanGuid, "Balanced", isActive: true);
        _power.AddPlan(StockPowerPlan.PowerSaver.PlanGuid, "Power saver");
        var change = PowerPlanChangeFactory.CreateStockPlanRestore(StockPowerPlan.PowerSaver);

        var result = await Module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(_power.Calls, c => c.StartsWith("RestoreDefaultScheme:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_PassesTheServiceFailureThrough()
    {
        _power.AddPlan(StockPowerPlan.Balanced.PlanGuid, "Balanced", isActive: true);
        _power.InjectFailure("RestoreDefaultScheme");
        var change = PowerPlanChangeFactory.CreateStockPlanRestore(StockPowerPlan.PowerSaver);

        var result = await Module.ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
    }

    [Fact]
    public async Task Revert_DeletesThePlanAgain()
    {
        _power.AddPlan(StockPowerPlan.Balanced.PlanGuid, "Balanced", isActive: true);
        _power.AddPlan(StockPowerPlan.PowerSaver.PlanGuid, "Power saver");
        var change = PowerPlanChangeFactory.CreateStockPlanRestore(StockPowerPlan.PowerSaver);
        var revert = change with { BeforeValue = "1", AfterValue = "0" };

        var result = await Module.RevertChangeAsync(revert);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Null(_power.GetPlan(StockPowerPlan.PowerSaver.PlanGuid));
    }

    [Fact]
    public async Task Revert_RefusesWhileActive()
    {
        _power.AddPlan(StockPowerPlan.PowerSaver.PlanGuid, "Power saver", isActive: true);
        var change = PowerPlanChangeFactory.CreateStockPlanRestore(StockPowerPlan.PowerSaver);
        var revert = change with { BeforeValue = "1", AfterValue = "0" };

        var result = await Module.RevertChangeAsync(revert);

        Assert.False(result.IsSuccess);
        Assert.Contains("active plan", result.ErrorMessage, StringComparison.Ordinal);
        Assert.NotNull(_power.GetPlan(StockPowerPlan.PowerSaver.PlanGuid));
    }
}
