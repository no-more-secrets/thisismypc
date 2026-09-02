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
    private readonly FakeRegistryService _registry = new();

    private PowerModule Module => new(_power, _registry);

    private static ChangeDescriptor ActivePlanChange(string beforeGuid, string afterGuid) =>
        PowerPlanChangeFactory.CreateActivePlanChange(
            new PowerPlan { PlanGuid = Guid.Parse(beforeGuid), Name = "Before", IsActive = true },
            new PowerPlan { PlanGuid = Guid.Parse(afterGuid), Name = "After", IsActive = false });

    [Fact]
    public async Task Apply_ActivePlanChange_MovesAGroupPolicyPinToTheTargetFirst()
    {
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        _power.AddPlan(HighPerformanceGuid, "High performance");
        _registry.WriteString(PowerPlanChangeFactory.ActivePlanPolicyKeyPath, PowerPlanChangeFactory.ActivePlanPolicyValueName, BalancedGuid.ToString("D"));

        var result = await Module.ApplyChangeAsync(
            ActivePlanChange(BalancedGuid.ToString("D"), HighPerformanceGuid.ToString("D")));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(HighPerformanceGuid.ToString("D"),
            _registry.ReadString(PowerPlanChangeFactory.ActivePlanPolicyKeyPath, PowerPlanChangeFactory.ActivePlanPolicyValueName).Value);
        Assert.Contains($"SetActivePlan:{HighPerformanceGuid:D}", _power.Calls);

        // Undo hands back the swapped descriptor: the pin follows the plan back.
        var undone = await Module.RevertChangeAsync(
            ActivePlanChange(HighPerformanceGuid.ToString("D"), BalancedGuid.ToString("D")));
        Assert.True(undone.IsSuccess, undone.ErrorMessage);
        Assert.Equal(BalancedGuid.ToString("D"),
            _registry.ReadString(PowerPlanChangeFactory.ActivePlanPolicyKeyPath, PowerPlanChangeFactory.ActivePlanPolicyValueName).Value);
    }

    [Fact]
    public async Task Apply_ActivePlanChange_LeavesTheRegistryAloneWithoutAPolicy()
    {
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        _power.AddPlan(HighPerformanceGuid, "High performance");
        var result = await Module.ApplyChangeAsync(
            ActivePlanChange(BalancedGuid.ToString("D"), HighPerformanceGuid.ToString("D")));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(_registry.ReadString(PowerPlanChangeFactory.ActivePlanPolicyKeyPath, PowerPlanChangeFactory.ActivePlanPolicyValueName).IsSuccess);
        Assert.DoesNotContain(_registry.Calls, c => c.StartsWith("WriteString:", StringComparison.Ordinal));
    }

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

    private static readonly Guid SubgroupGuid = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid SettingGuid = new("893dee8e-2bef-41e0-89c6-b55d0929964c");

    private static PowerSetting RangeSetting => new()
    {
        SubgroupGuid = SubgroupGuid,
        SubgroupName = "Processor power management",
        SettingGuid = SettingGuid,
        Name = "Minimum processor state",
        IsRange = true,
        Min = 0,
        Max = 100,
        Units = "%",
        AcIndex = 5,
        DcIndex = 5,
    };

    [Fact]
    public async Task Apply_SettingChange_ParsesLocationAndWrites()
    {
        var plan = new PowerPlan { PlanGuid = BalancedGuid, Name = "Balanced", IsActive = true };
        var change = PowerPlanChangeFactory.CreateSettingChange(plan, RangeSetting, ac: true, currentIndex: 5, newIndex: 100);

        var result = await Module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        Assert.Equal(100u, _power.WrittenIndexes[$"{BalancedGuid:D}/{SubgroupGuid:D}/{SettingGuid:D}/AC"]);
    }

    [Fact]
    public async Task Apply_SettingChange_DcScope_WritesDc()
    {
        var plan = new PowerPlan { PlanGuid = BalancedGuid, Name = "Balanced", IsActive = true };
        var change = PowerPlanChangeFactory.CreateSettingChange(plan, RangeSetting, ac: false, currentIndex: 5, newIndex: 0);

        var result = await Module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        Assert.Equal(0u, _power.WrittenIndexes[$"{BalancedGuid:D}/{SubgroupGuid:D}/{SettingGuid:D}/DC"]);
    }

    [Fact]
    public async Task Apply_SettingChange_BogusLocation_FailsWithNotFound()
    {
        var plan = new PowerPlan { PlanGuid = BalancedGuid, Name = "Balanced", IsActive = true };
        var change = PowerPlanChangeFactory.CreateSettingChange(plan, RangeSetting, ac: true, currentIndex: 5, newIndex: 100)
            with { SystemLocation = "garbage" };

        var result = await Module.ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.NotFound, result.ErrorCategory);
        Assert.Empty(_power.WrittenIndexes);
    }

    [Fact]
    public async Task Apply_ModernStandbyDisable_WritesDWordZero()
    {
        var change = PowerPlanChangeFactory.CreateModernStandbyToggle(currentValue: null, disable: true);

        var result = await Module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _registry.GetDWord(
            PowerPlanChangeFactory.ModernStandbyKeyPath, PowerPlanChangeFactory.ModernStandbyValueName));
    }

    [Fact]
    public async Task Revert_ModernStandby_SwappedDescriptor_DeletesTheValue()
    {
        _registry.SetDWord(PowerPlanChangeFactory.ModernStandbyKeyPath, PowerPlanChangeFactory.ModernStandbyValueName, 0);

        // Revert contract: Before/After arrive pre-swapped; empty AfterValue = delete
        var change = PowerPlanChangeFactory.CreateModernStandbyToggle(currentValue: null, disable: true)
            with { BeforeValue = "0", AfterValue = string.Empty };

        var result = await Module.RevertChangeAsync(change);

        Assert.True(result.IsSuccess);
        Assert.Null(_registry.GetDWord(
            PowerPlanChangeFactory.ModernStandbyKeyPath, PowerPlanChangeFactory.ModernStandbyValueName));
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
