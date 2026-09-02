using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.Modules.Power.Services;

/// <summary>Enumerates registered power plans via IPowerService.</summary>
public sealed class PowerPlanScanner
{
    private readonly IPowerService _powerService;

    public PowerPlanScanner(IPowerService powerService)
    {
        _powerService = powerService;
    }

    /// <summary>Non-null after Scan() when plan enumeration itself failed (list is then empty).</summary>
    public string? LastScanError { get; private set; }

    /// <summary>
    /// A plan counts as Ultimate Performance when it carries our marker
    /// description (locale-proof), or matches the hidden source GUID or name
    /// (installed by something else, e.g. winutil or a Workstation SKU).
    /// Display/idempotency only; deletion must use <see cref="FindMarkedUltimatePerformance"/>.
    /// </summary>
    public static PowerPlan? FindUltimatePerformance(IReadOnlyList<PowerPlan> plans) =>
        FindMarkedUltimatePerformance(plans)
        ?? plans.FirstOrDefault(p =>
            p.PlanGuid == Changes.PowerPlanChangeFactory.UltimatePerformanceSourceGuid
            || p.Name.Equals("Ultimate Performance", StringComparison.OrdinalIgnoreCase));

    /// <summary>Only a plan ThisIsMyPC created (marker description); the sole legal deletion target.</summary>
    public static PowerPlan? FindMarkedUltimatePerformance(IReadOnlyList<PowerPlan> plans) =>
        plans.FirstOrDefault(p => p.Description == Changes.PowerPlanChangeFactory.UltimatePerformanceMarker);

    /// <summary>A plan the person created through this app (marker description) with the given name.</summary>
    public static PowerPlan? FindCreatedPlan(IReadOnlyList<PowerPlan> plans, string name) =>
        plans.FirstOrDefault(p => p.Description == Changes.PowerPlanChangeFactory.CreatedPlanMarker
            && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<PowerPlan> Scan()
    {
        LastScanError = null;
        var enumerated = _powerService.EnumeratePlans();
        if (!enumerated.IsSuccess || enumerated.Value is null)
        {
            LastScanError = enumerated.ErrorMessage ?? "Power plan enumeration failed.";
            return [];
        }

        return enumerated.Value
            .Select(info => new PowerPlan
            {
                PlanGuid = info.PlanGuid,
                Name = info.Name,
                Description = info.Description,
                IsActive = info.IsActive,
            })
            .ToList();
    }
}
