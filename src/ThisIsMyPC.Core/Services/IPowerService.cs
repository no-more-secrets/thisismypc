using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

/// <summary>
/// One registered power scheme. Name falls back to the GUID string when the
/// scheme has no friendly name; Description is null when missing (common for
/// OEM plans).
/// </summary>
public sealed record PowerPlanInfo(
    Guid PlanGuid,
    string Name,
    string? Description,
    bool IsActive);

/// <summary>
/// Power plan access via powrprof.dll. Enumeration returns every registered
/// scheme, including plans Control Panel normally hides (e.g. Ultimate
/// Performance); on Modern Standby machines the list may contain only Balanced.
/// </summary>
public interface IPowerService
{
    OperationResult<IReadOnlyList<PowerPlanInfo>> EnumeratePlans();

    OperationResult<bool> SetActivePlan(Guid planGuid);
}
