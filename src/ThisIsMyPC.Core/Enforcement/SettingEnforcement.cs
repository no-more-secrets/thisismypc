using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Enforcement;

public record SettingEnforcement
{
    public IReadOnlyList<string>? CompanionServices { get; init; }
    public IReadOnlyList<string>? CompanionTasks { get; init; }
    public IReadOnlyList<string>? GPCacheEntries { get; init; }
    public IReadOnlyList<string>? ReversionVectors { get; init; }
    public WindowsSku? SkuRestriction { get; init; }
    public bool OwnerModeRequired { get; init; }
    public bool AclElevation { get; init; }
}
