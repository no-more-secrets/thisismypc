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

    /// <summary>
    /// True while the power service refuses every plan switch because a
    /// Group Policy pinned the active plan when the service started. The
    /// registry value may already be gone; the service keeps its copy until
    /// the next restart.
    /// </summary>
    bool IsActivePlanLockedByPolicy() => false;

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

    /// <summary>Enables or disables hibernation (the hiberfile), like powercfg /hibernate.</summary>
    OperationResult<bool> SetHibernateEnabled(bool enable);

    /// <summary>Duplicates a scheme (including hidden ones); returns the new plan's GUID.</summary>
    OperationResult<Guid> DuplicateScheme(Guid sourceSchemeGuid);

    /// <summary>
    /// Duplicates a scheme into a GUID of the caller's choosing. The source
    /// must still be registered (a deleted plan is not found), and the
    /// destination must not exist yet.
    /// </summary>
    OperationResult<Guid> DuplicateSchemeAs(Guid sourceSchemeGuid, Guid destinationSchemeGuid) =>
        OperationResult<Guid>.Failure("Duplicating into a chosen GUID is not supported here.", ErrorCategory.ServiceUnavailable);

    /// <summary>
    /// Puts a stock scheme (Balanced, High performance, Power saver) back
    /// from Windows' default store under its own GUID. When the scheme still
    /// exists this resets its settings to defaults, so callers check first.
    /// </summary>
    OperationResult<bool> RestoreDefaultScheme(Guid schemeGuid) =>
        OperationResult<bool>.Failure("Restoring a stock plan is not supported here.", ErrorCategory.ServiceUnavailable);

    /// <summary>Deletes a registered scheme. Fails while the scheme is active.</summary>
    OperationResult<bool> DeleteScheme(Guid schemeGuid);

    /// <summary>Sets a scheme's friendly name and description.</summary>
    OperationResult<bool> WriteSchemeText(Guid schemeGuid, string name, string description);
}
