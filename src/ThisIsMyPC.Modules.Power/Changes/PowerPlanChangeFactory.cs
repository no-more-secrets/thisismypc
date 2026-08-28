using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.Modules.Power.Changes;

/// <summary>Builds ChangeDescriptors that switch the active power plan via powrprof.dll.</summary>
public static class PowerPlanChangeFactory
{
    public const string ModuleId = "Power Plans";

    /// <summary>The active plan is one logical setting — re-selection re-stages this id.</summary>
    public const string ActivePlanSettingId = "active-power-plan";

    public static ChangeDescriptor CreateActivePlanChange(PowerPlan currentActive, PowerPlan newPlan)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = ActivePlanSettingId,
            DisplayName = $"Active power plan: {newPlan.Name}",
            SystemLocation = "powrprof:ActiveScheme",
            BeforeValue = currentActive.PlanGuid.ToString("D"),
            AfterValue = newPlan.PlanGuid.ToString("D"),
            BeforeDisplay = currentActive.Name,
            AfterDisplay = newPlan.Name,
            ValueType = ChangeValueType.PowerPlan_Setting,
            Category = ChangeCategory.Modify,
            RestartRequirement = RestartRequirement.None,
        };
    }
}
