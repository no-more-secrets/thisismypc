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

    public bool HibernateEnabled { get; set; } = true;

    public OperationResult<bool> SetHibernateEnabled(bool enable)
    {
        Calls.Add($"SetHibernateEnabled:{enable}");
        if (_failures.TryGetValue("SetHibernateEnabled", out var fail))
            return OperationResult<bool>.Failure("Injected SetHibernateEnabled failure.", fail);
        HibernateEnabled = enable;
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<Guid> DuplicateScheme(Guid sourceSchemeGuid)
    {
        Calls.Add($"DuplicateScheme:{sourceSchemeGuid:D}");
        if (_failures.TryGetValue("DuplicateScheme", out var fail))
            return OperationResult<Guid>.Failure("Injected DuplicateScheme failure.", fail);
        var newGuid = Guid.NewGuid();
        _plans.Add(new PowerPlanInfo(newGuid, "Duplicated plan", null, IsActive: false));
        return OperationResult<Guid>.Success(newGuid);
    }

    public OperationResult<bool> RestoreDefaultScheme(Guid schemeGuid)
    {
        Calls.Add($"RestoreDefaultScheme:{schemeGuid:D}");
        if (_failures.TryGetValue("RestoreDefaultScheme", out var fail))
            return OperationResult<bool>.Failure("Injected RestoreDefaultScheme failure.", fail);
        var stock = Modules.Power.Models.StockPowerPlan.FindByGuid(schemeGuid);
        if (stock is null)
            return OperationResult<bool>.Failure($"No default power plan '{schemeGuid:D}'.", ErrorCategory.NotFound);
        _plans.RemoveAll(p => p.PlanGuid == schemeGuid);
        _plans.Add(new PowerPlanInfo(schemeGuid, stock.Name, null, IsActive: false));
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<Guid> DuplicateSchemeAs(Guid sourceSchemeGuid, Guid destinationSchemeGuid)
    {
        Calls.Add($"DuplicateSchemeAs:{sourceSchemeGuid:D}->{destinationSchemeGuid:D}");
        if (_failures.TryGetValue("DuplicateSchemeAs", out var fail))
            return OperationResult<Guid>.Failure("Injected DuplicateSchemeAs failure.", fail);
        if (_plans.Any(p => p.PlanGuid == destinationSchemeGuid))
            return OperationResult<Guid>.Failure($"Power plan '{destinationSchemeGuid:D}' already exists.", ErrorCategory.ServiceUnavailable);
        var stock = Modules.Power.Models.StockPowerPlan.FindByGuid(sourceSchemeGuid);
        _plans.Add(new PowerPlanInfo(destinationSchemeGuid, stock?.Name ?? "Duplicated plan", null, IsActive: false));
        return OperationResult<Guid>.Success(destinationSchemeGuid);
    }

    public OperationResult<bool> DeleteScheme(Guid schemeGuid)
    {
        Calls.Add($"DeleteScheme:{schemeGuid:D}");
        if (_failures.TryGetValue("DeleteScheme", out var fail))
            return OperationResult<bool>.Failure("Injected DeleteScheme failure.", fail);
        var removed = _plans.RemoveAll(p => p.PlanGuid == schemeGuid) > 0;
        return removed
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure($"No power plan '{schemeGuid:D}'.", ErrorCategory.NotFound);
    }

    public OperationResult<bool> WriteSchemeText(Guid schemeGuid, string name, string description)
    {
        Calls.Add($"WriteSchemeText:{schemeGuid:D}:{name}");
        if (_failures.TryGetValue("WriteSchemeText", out var fail))
            return OperationResult<bool>.Failure("Injected WriteSchemeText failure.", fail);
        var index = _plans.FindIndex(p => p.PlanGuid == schemeGuid);
        if (index < 0)
            return OperationResult<bool>.Failure($"No power plan '{schemeGuid:D}'.", ErrorCategory.NotFound);
        _plans[index] = _plans[index] with { Name = name, Description = description };
        return OperationResult<bool>.Success(true);
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
