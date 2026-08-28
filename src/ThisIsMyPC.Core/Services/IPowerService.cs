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

/// <summary>One entry of an enumerated power setting: the writable index and its label.</summary>
public sealed record PowerPossibleValue(uint Index, string Name);

/// <summary>
/// One individual setting of a power plan. Values are powrprof "value indexes":
/// range settings interpret them directly (in <see cref="Units"/>), enumerated
/// settings as an index into <see cref="PossibleValues"/>. AcIndex/DcIndex are
/// null when the per-plan value could not be read (best-effort).
/// </summary>
public sealed record PowerSettingInfo(
    Guid SubgroupGuid,
    string SubgroupName,
    Guid SettingGuid,
    string Name,
    string? Description,
    uint? AcIndex,
    uint? DcIndex,
    string? Units,
    bool IsRange,
    uint Min,
    uint Max,
    uint Increment,
    IReadOnlyList<PowerPossibleValue> PossibleValues);

/// <summary>
/// Power plan access via powrprof.dll. Enumeration returns every registered
/// scheme, including plans Control Panel normally hides (e.g. Ultimate
/// Performance); on Modern Standby machines the list may contain only Balanced.
/// </summary>
public interface IPowerService
{
    OperationResult<IReadOnlyList<PowerPlanInfo>> EnumeratePlans();

    OperationResult<bool> SetActivePlan(Guid planGuid);

    /// <summary>All individual settings of one plan, grouped by subgroup in enumeration order.</summary>
    OperationResult<IReadOnlyList<PowerSettingInfo>> EnumeratePlanSettings(Guid planGuid);

    /// <summary>
    /// Writes one AC or DC value index. When the target plan is the active
    /// scheme, the implementation re-activates it so the change takes effect
    /// immediately.
    /// </summary>
    OperationResult<bool> WriteSettingIndex(Guid planGuid, Guid subgroupGuid, Guid settingGuid, bool ac, uint valueIndex);

    /// <summary>True when the platform supports Modern Standby (AoAc).</summary>
    bool SupportsModernStandby();
}
