using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Enforcement;

public record SettingEnforcement
{
    public IReadOnlyList<string>? CompanionServices { get; init; }
    public IReadOnlyList<string>? CompanionTasks { get; init; }
    public IReadOnlyList<string>? GPCacheEntries { get; init; }
    public IReadOnlyList<string>? ReversionVectors { get; init; }
    /// <summary>
    /// Minimum edition tier that honors the policy (Home &lt; Pro &lt;
    /// Enterprise/Education). Editions below it apply the write but Windows ignores it.
    /// Informational only — never gates staging (FR129).
    /// </summary>
    public WindowsSku? SkuRestriction { get; init; }
    public bool OwnerModeRequired { get; init; }
    public bool AclElevation { get; init; }
}
