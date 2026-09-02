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
        plans.OrderBy(p => p, Comparer).ToList();

    public static IComparer<PowerPlan> Comparer { get; } = Comparer<PowerPlan>.Create(Compare);

    public static int Compare(PowerPlan left, PowerPlan right)
    {
        var byRank = Rank(left).CompareTo(Rank(right));
        return byRank != 0
            ? byRank
            : string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
    }

    private static int Rank(PowerPlan plan)
    {
        if (plan.IsActive)
            return 0;
        var stock = StockPowerPlan.All.ToList().FindIndex(s => s.PlanGuid == plan.PlanGuid);
        if (stock >= 0)
            return 1 + stock;
        if (PowerPlanScanner.FindUltimatePerformance([plan]) is not null)
            return 1 + StockPowerPlan.All.Count;
        return 2 + StockPowerPlan.All.Count;
    }
}
