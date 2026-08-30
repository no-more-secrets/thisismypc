using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.Modules.Power.Actions;

/// <summary>
/// One-way power plan actions. Deleting a plan cannot restore its custom
/// settings, so it goes through the pending-actions queue, never the
/// reversible pipeline.
/// </summary>
public static class PowerActionFactory
{
    public const string DeletePlanPrefix = "delete-plan:";

    public static ActionDescriptor CreateDeletePlan(PowerPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new ActionDescriptor
        {
            ModuleId = "Power Plans",
            ActionId = DeletePlanPrefix + plan.PlanGuid.ToString("D"),
            DisplayName = $"Delete power plan {plan.Name}",
            Detail = $"guid: {plan.PlanGuid:D} (custom settings are lost)",
            UndoHint = "Recreate the plan manually, or restore the stock plans with powercfg -restoredefaultschemes.",
        };
    }
}
