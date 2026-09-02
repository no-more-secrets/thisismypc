namespace ThisIsMyPC.Modules.Power.Models;

public sealed record PowerPlan
{
    /// <summary>Schemes Windows registers but hides from Control Panel on most SKUs.</summary>
    private static readonly HashSet<Guid> NormallyHiddenGuids =
    [
        new("e9a42b02-d5df-448d-aa00-03f14749eb61"), // Ultimate Performance
    ];

    public required Guid PlanGuid { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required bool IsActive { get; init; }

    public bool IsNormallyHidden => NormallyHiddenGuids.Contains(PlanGuid);
}

/// <summary>Aggregate scan result for the Power Plans module.</summary>
/// <param name="HibernateEnabled">Null when the state could not be read.</param>
/// <param name="UltimatePerformancePlan">The registered Ultimate Performance plan, when one exists.</param>
/// <param name="PolicyPinnedPlan">The plan a Group Policy value pins as active, when the value exists.</param>
/// <param name="ActivePlanLockedByPolicy">True while the power service refuses plan switches until a restart.</param>
public sealed record PowerScanData(
    IReadOnlyList<PowerPlan> Plans,
    string? ScanError = null,
    bool? HibernateEnabled = null,
    PowerPlan? UltimatePerformancePlan = null,
    Guid? PolicyPinnedPlan = null,
    bool ActivePlanLockedByPolicy = false);
