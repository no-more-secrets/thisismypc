using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.UiTests.Fakes;

/// <summary>
/// The no-op power service every CI-safe power and display shot shares:
/// every write succeeds, duplicates get a fresh GUID, and a plan's settings
/// are whatever the test hands in.
/// </summary>
public sealed class UiFakePowerService(IReadOnlyList<PowerSettingInfo>? settings = null) : IPowerService
{
    public OperationResult<IReadOnlyList<PowerPlanInfo>> EnumeratePlans() =>
        OperationResult<IReadOnlyList<PowerPlanInfo>>.Success([]);
    public OperationResult<bool> SetActivePlan(Guid planGuid) => OperationResult<bool>.Success(true);
    public OperationResult<IReadOnlyList<PowerSettingInfo>> EnumeratePlanSettings(Guid planGuid) =>
        OperationResult<IReadOnlyList<PowerSettingInfo>>.Success(settings ?? []);
    public OperationResult<bool> WriteSettingIndex(Guid planGuid, Guid subgroupGuid, Guid settingGuid, bool ac, uint valueIndex) =>
        OperationResult<bool>.Success(true);
    public bool SupportsModernStandby() => false;
    public OperationResult<bool> SetHibernateEnabled(bool enable) => OperationResult<bool>.Success(true);
    public OperationResult<Guid> DuplicateScheme(Guid sourceSchemeGuid) => OperationResult<Guid>.Success(Guid.NewGuid());
    public OperationResult<bool> DeleteScheme(Guid schemeGuid) => OperationResult<bool>.Success(true);
    public OperationResult<bool> WriteSchemeText(Guid schemeGuid, string name, string description) =>
        OperationResult<bool>.Success(true);
}
