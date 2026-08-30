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
    /// Informational only; never gates staging (FR129).
    /// </summary>
    public WindowsSku? SkuRestriction { get; init; }
    public bool OwnerModeRequired { get; init; }
    public bool AclElevation { get; init; }

    /// <summary>
    /// Direction of companion handling. False (default): this change hardens; the
    /// executor disables companion services/tasks around the primary mutation.
    /// True: this change restores; the executor re-enables them (services
    /// Disabled → Manual) after the primary mutation. Reverting a change runs the
    /// opposite direction, so undo stays symmetric either way.
    /// </summary>
    public bool RestoresCompanions { get; init; }
}
