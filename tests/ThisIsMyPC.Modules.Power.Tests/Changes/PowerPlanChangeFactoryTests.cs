using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Power.Changes;
using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.Modules.Power.Tests.Changes;

public sealed class PowerPlanChangeFactoryTests
{
    private static readonly Guid BalancedGuid = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid HighPerformanceGuid = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    [Fact]
    public void CreateActivePlanChange_PopulatesAllFields()
    {
        var balanced = new PowerPlan { PlanGuid = BalancedGuid, Name = "Balanced", IsActive = true };
        var high = new PowerPlan { PlanGuid = HighPerformanceGuid, Name = "High performance", IsActive = false };

        var change = PowerPlanChangeFactory.CreateActivePlanChange(balanced, high);

        Assert.Equal("Power Plans", change.ModuleId);
        Assert.Equal(PowerPlanChangeFactory.ActivePlanSettingId, change.SettingId);
        Assert.Equal("Active power plan: High performance", change.DisplayName);
        Assert.Equal("powrprof:ActiveScheme", change.SystemLocation);
        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", change.BeforeValue);
        Assert.Equal("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", change.AfterValue);
        Assert.Equal("Balanced", change.BeforeDisplay);
        Assert.Equal("High performance", change.AfterDisplay);
        Assert.Equal(ChangeValueType.PowerPlan_Setting, change.ValueType);
        Assert.Equal(ChangeCategory.Modify, change.Category);
        Assert.Equal(RestartRequirement.None, change.RestartRequirement);
        Assert.Null(change.Enforcement);
    }

    private static readonly Guid SubgroupGuid = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid SettingGuid = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

    [Fact]
    public void CreateSettingChange_PopulatesAllFields_EnumeratedDisplays()
    {
        var plan = new PowerPlan { PlanGuid = BalancedGuid, Name = "Balanced", IsActive = true };
        var setting = new PowerSetting
        {
            SubgroupGuid = SubgroupGuid,
            SubgroupName = "Sleep",
            SettingGuid = SettingGuid,
            Name = "Sleep after",
            IsRange = false,
            PossibleValues = [new(0, "Never"), new(1, "1 minute")],
            AcIndex = 0,
            DcIndex = 1,
        };

        var change = PowerPlanChangeFactory.CreateSettingChange(plan, setting, ac: false, currentIndex: 1, newIndex: 0);

        Assert.Equal("Power Plans", change.ModuleId);
        Assert.Equal($"power-setting:{BalancedGuid:D}:{SettingGuid:D}:DC", change.SettingId);
        Assert.Equal($"{BalancedGuid:D}/{SubgroupGuid:D}/{SettingGuid:D}/DC", change.SystemLocation);
        Assert.Equal("Balanced: Sleep after (On battery)", change.DisplayName);
        Assert.Equal("1", change.BeforeValue);
        Assert.Equal("0", change.AfterValue);
        Assert.Equal("1 minute", change.BeforeDisplay);
        Assert.Equal("Never", change.AfterDisplay);
        Assert.Equal(ChangeValueType.PowerPlan_Setting, change.ValueType);
        Assert.Null(change.Enforcement);
    }

    [Fact]
    public void CreateSettingChange_RangeDisplays_UseUnits()
    {
        var plan = new PowerPlan { PlanGuid = BalancedGuid, Name = "Balanced", IsActive = true };
        var setting = new PowerSetting
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
        };

        var change = PowerPlanChangeFactory.CreateSettingChange(plan, setting, ac: true, currentIndex: 5, newIndex: 100);

        Assert.Equal($"power-setting:{BalancedGuid:D}:{SettingGuid:D}:AC", change.SettingId);
        Assert.Equal("5 %", change.BeforeDisplay);
        Assert.Equal("100 %", change.AfterDisplay);
        Assert.Equal("Balanced: Minimum processor state (Plugged in)", change.DisplayName);
    }

    [Fact]
    public void CreateModernStandbyToggle_Disable_FromAbsent()
    {
        var change = PowerPlanChangeFactory.CreateModernStandbyToggle(currentValue: null, disable: true);

        Assert.Equal("modern-standby", change.SettingId);
        Assert.Equal(@"HKLM\SYSTEM\CurrentControlSet\Control\Power\PlatformAoAcOverride", change.SystemLocation);
        Assert.Equal(string.Empty, change.BeforeValue); // absent; revert deletes
        Assert.Equal("0", change.AfterValue);
        Assert.Equal(ChangeValueType.Registry_DWord, change.ValueType);
        Assert.Equal(ChangeCategory.Disable, change.Category);
        Assert.Equal(RestartRequirement.Reboot, change.RestartRequirement);
    }

    [Fact]
    public void CreateModernStandbyToggle_Restore_DeletesValue()
    {
        var change = PowerPlanChangeFactory.CreateModernStandbyToggle(currentValue: 0, disable: false);

        Assert.Equal("0", change.BeforeValue);
        Assert.Equal(string.Empty, change.AfterValue); // empty = delete → Windows default
        Assert.Equal(ChangeCategory.Enable, change.Category);
    }
}
