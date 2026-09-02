namespace ThisIsMyPC.Modules.Power.Models;

/// <summary>
/// A plan Windows ships and can recreate from its built-in defaults under
/// the same GUID after someone deleted it. Ultimate Performance is not here:
/// Windows keeps it hidden under its stock GUID, so it is added as a marked
/// copy instead (see PowerPlanChangeFactory.CreateUltimatePerformanceToggle).
/// </summary>
public sealed record StockPowerPlan(Guid PlanGuid, string Name)
{
    public static readonly StockPowerPlan Balanced = new(new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"), "Balanced");
    public static readonly StockPowerPlan HighPerformance = new(new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), "High performance");
    public static readonly StockPowerPlan PowerSaver = new(new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"), "Power saver");

    /// <summary>In Control Panel order.</summary>
    public static IReadOnlyList<StockPowerPlan> All { get; } = [Balanced, HighPerformance, PowerSaver];

    public static StockPowerPlan? FindByGuid(Guid guid) => All.FirstOrDefault(p => p.PlanGuid == guid);
}
