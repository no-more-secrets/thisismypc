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
}
