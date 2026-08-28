namespace ThisIsMyPC.Modules.Power.Models;

public sealed record PowerPlan
{
    /// <summary>Schemes Windows registers but hides from Control Panel on most SKUs.</summary>
    private static readonly HashSet<Guid> NormallyHiddenGuids =
    [
        new("e9a42b02-d5df-448d-aa66-1f0000e60cc8"), // Ultimate Performance
    ];

    public required Guid PlanGuid { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required bool IsActive { get; init; }

    public bool IsNormallyHidden => NormallyHiddenGuids.Contains(PlanGuid);
}

/// <summary>Aggregate scan result for the Power Plans module.</summary>
public sealed record PowerScanData(
    IReadOnlyList<PowerPlan> Plans,
    string? ScanError = null);
