using ThisIsMyPC.Core.Enforcement;

namespace ThisIsMyPC.Core.Sets;

/// <summary>
/// One desired setting state inside a set. Entries target module settings by
/// (ModuleId, SettingId); the owning module knows the value type, and before-values
/// are captured from the live system at staging time — never stored in set files.
/// </summary>
public sealed record SetEntry
{
    public required string ModuleId { get; init; }
    public required string SettingId { get; init; }

    /// <summary>Desired value, string-typed like ChangeDescriptor values.</summary>
    public required string Value { get; init; }

    public required string Description { get; init; }

    /// <summary>Optional human-readable rendering of <see cref="Value"/> ("Hidden", "Disabled").</summary>
    public string? DisplayValue { get; init; }

    /// <summary>
    /// Optional constituent-set label for optimization packs — the preview groups
    /// entries by this so users see which bundle each change comes from.
    /// </summary>
    public string? Group { get; init; }

    public SettingEnforcement? Enforcement { get; init; }
}
