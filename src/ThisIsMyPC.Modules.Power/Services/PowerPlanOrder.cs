using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.Modules.Power.Services;

/// <summary>
/// The order the plan list shows: the active plan, then the Windows stock
/// plans as Control Panel lists them (Balanced, Power saver, High
/// performance), then Ultimate Performance, then everything else by name.
/// Windows enumerates plans by GUID, which reads as random.
/// </summary>
public static class PowerPlanOrder
{
    public static IReadOnlyList<PowerPlan> Sort(IEnumerable<PowerPlan> plans) =>
        plans.OrderBy(Rank).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public static int Rank(PowerPlan plan) => Rank(plan, plan.IsActive);

    /// <summary>Rank with the active flag supplied, for rows whose live state has moved on from the scan.</summary>
    public static int Rank(PowerPlan plan, bool isActive)
    {
        if (isActive)
            return 0;
        var stock = StockPowerPlan.All;
        for (var i = 0; i < stock.Count; i++)
        {
            if (stock[i].PlanGuid == plan.PlanGuid)
                return 1 + i;
        }
        return PowerPlanScanner.IsUltimatePerformance(plan) ? 1 + stock.Count : 2 + stock.Count;
    }
}
