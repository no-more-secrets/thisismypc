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
