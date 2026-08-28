using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Power.Tests.Fakes;

/// <summary>
/// Scriptable in-memory IPowerService (per-project fake convention). Seed plans
/// with <see cref="AddPlan"/>; operations mutate in-memory state and are
/// recorded in <see cref="Calls"/>.
/// </summary>
public sealed class FakePowerService : IPowerService
{
    private readonly List<PowerPlanInfo> _plans = [];
    private readonly Dictionary<string, ErrorCategory> _failures = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Calls { get; } = [];

    public void AddPlan(Guid guid, string name, string? description = null, bool isActive = false)
        => _plans.Add(new PowerPlanInfo(guid, name, description, isActive));

    public PowerPlanInfo? GetPlan(Guid guid) => _plans.FirstOrDefault(p => p.PlanGuid == guid);

    public void InjectFailure(string operation, ErrorCategory category = ErrorCategory.AccessDenied)
        => _failures[operation] = category;

    public OperationResult<IReadOnlyList<PowerPlanInfo>> EnumeratePlans()
    {
        Calls.Add("EnumeratePlans");
        if (_failures.TryGetValue("EnumeratePlans", out var fail))
            return OperationResult<IReadOnlyList<PowerPlanInfo>>.Failure("Injected EnumeratePlans failure.", fail);
        return OperationResult<IReadOnlyList<PowerPlanInfo>>.Success(_plans.ToList());
    }

    private readonly List<PowerSettingInfo> _settings = [];

    public bool ModernStandbySupported { get; set; }

    public void AddSetting(PowerSettingInfo setting) => _settings.Add(setting);

    /// <summary>Written value indexes: key "plan/subgroup/setting/AC|DC" → index.</summary>
    public Dictionary<string, uint> WrittenIndexes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public OperationResult<IReadOnlyList<PowerSettingInfo>> EnumeratePlanSettings(Guid planGuid)
    {
        Calls.Add($"EnumeratePlanSettings:{planGuid:D}");
        if (_failures.TryGetValue("EnumeratePlanSettings", out var fail))
            return OperationResult<IReadOnlyList<PowerSettingInfo>>.Failure("Injected EnumeratePlanSettings failure.", fail);
        return OperationResult<IReadOnlyList<PowerSettingInfo>>.Success(_settings.ToList());
    }

    public OperationResult<bool> WriteSettingIndex(Guid planGuid, Guid subgroupGuid, Guid settingGuid, bool ac, uint valueIndex)
    {
        var scope = ac ? "AC" : "DC";
        Calls.Add($"WriteSettingIndex:{planGuid:D}/{subgroupGuid:D}/{settingGuid:D}/{scope}={valueIndex}");
        if (_failures.TryGetValue("WriteSettingIndex", out var fail))
            return OperationResult<bool>.Failure("Injected WriteSettingIndex failure.", fail);
        WrittenIndexes[$"{planGuid:D}/{subgroupGuid:D}/{settingGuid:D}/{scope}"] = valueIndex;
        return OperationResult<bool>.Success(true);
    }

    public bool SupportsModernStandby()
    {
        Calls.Add("SupportsModernStandby");
        return ModernStandbySupported;
    }

    public OperationResult<bool> SetActivePlan(Guid planGuid)
    {
        Calls.Add($"SetActivePlan:{planGuid:D}");
        if (_failures.TryGetValue("SetActivePlan", out var fail))
            return OperationResult<bool>.Failure("Injected SetActivePlan failure.", fail);
        var index = _plans.FindIndex(p => p.PlanGuid == planGuid);
        if (index < 0)
            return OperationResult<bool>.Failure($"No power plan '{planGuid:D}'.", ErrorCategory.NotFound);
        for (var i = 0; i < _plans.Count; i++)
            _plans[i] = _plans[i] with { IsActive = i == index };
        return OperationResult<bool>.Success(true);
    }
}
