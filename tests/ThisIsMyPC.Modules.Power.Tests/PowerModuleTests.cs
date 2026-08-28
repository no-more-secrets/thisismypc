using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Modules.Power.Changes;
using ThisIsMyPC.Modules.Power.Models;
using ThisIsMyPC.Modules.Power.Tests.Fakes;

namespace ThisIsMyPC.Modules.Power.Tests;

public sealed class PowerModuleTests
{
    private static readonly Guid BalancedGuid = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid HighPerformanceGuid = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    private readonly FakePowerService _power = new();

    private PowerModule Module => new(_power);

    private static ChangeDescriptor ActivePlanChange(string beforeGuid, string afterGuid) =>
        PowerPlanChangeFactory.CreateActivePlanChange(
            new PowerPlan { PlanGuid = Guid.Parse(beforeGuid), Name = "Before", IsActive = true },
            new PowerPlan { PlanGuid = Guid.Parse(afterGuid), Name = "After", IsActive = false });

    [Fact]
    public async Task CheckAvailability_AlwaysAvailable()
    {
        var availability = await Module.CheckAvailabilityAsync();
        Assert.True(availability.IsAvailable);
    }

    [Fact]
    public async Task Scan_ReturnsPowerScanData()
    {
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        _power.AddPlan(HighPerformanceGuid, "High performance");

        var result = await Module.ScanSystemStateAsync();

        Assert.True(result.IsSuccess);
        var data = Assert.IsType<PowerScanData>(result.Value);
        Assert.Equal(2, data.Plans.Count);
        Assert.Null(data.ScanError);
        Assert.True(data.Plans.Single(p => p.PlanGuid == BalancedGuid).IsActive);
    }

    [Fact]
    public async Task Scan_EnumerationFailure_SurfacesScanError()
    {
        _power.InjectFailure("EnumeratePlans");

        var result = await Module.ScanSystemStateAsync();

        Assert.True(result.IsSuccess);
        var data = Assert.IsType<PowerScanData>(result.Value);
        Assert.Empty(data.Plans);
        Assert.NotNull(data.ScanError);
    }

    [Fact]
    public async Task Apply_ActivePlanChange_CallsSetActivePlan()
    {
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        _power.AddPlan(HighPerformanceGuid, "High performance");

        var result = await Module.ApplyChangeAsync(
            ActivePlanChange(BalancedGuid.ToString("D"), HighPerformanceGuid.ToString("D")));

        Assert.True(result.IsSuccess);
        Assert.Contains($"SetActivePlan:{HighPerformanceGuid:D}", _power.Calls);
        Assert.True(_power.GetPlan(HighPerformanceGuid)!.IsActive);
    }

    [Fact]
    public async Task Revert_SwappedDescriptor_RestoresPreviousPlan()
    {
        _power.AddPlan(BalancedGuid, "Balanced");
        _power.AddPlan(HighPerformanceGuid, "High performance", isActive: true);

        // Revert contract: Before/After arrive pre-swapped
        var reverted = await Module.RevertChangeAsync(
            ActivePlanChange(HighPerformanceGuid.ToString("D"), BalancedGuid.ToString("D")));

        Assert.True(reverted.IsSuccess);
        Assert.True(_power.GetPlan(BalancedGuid)!.IsActive);
    }

    [Fact]
    public async Task Apply_BogusGuid_FailsWithNotFound()
    {
        var change = ActivePlanChange(BalancedGuid.ToString("D"), HighPerformanceGuid.ToString("D"))
            with { AfterValue = "not-a-guid" };

        var result = await Module.ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.NotFound, result.ErrorCategory);
        Assert.Empty(_power.Calls);
    }

    [Fact]
    public async Task Apply_UnsupportedValueType_Fails()
    {
        var change = ActivePlanChange(BalancedGuid.ToString("D"), HighPerformanceGuid.ToString("D"))
            with { ValueType = ChangeValueType.Registry_DWord };

        var result = await Module.ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Apply_UnknownSettingId_Fails()
    {
        var change = ActivePlanChange(BalancedGuid.ToString("D"), HighPerformanceGuid.ToString("D"))
            with { SettingId = "some-other-setting" };

        var result = await Module.ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
    }
}
