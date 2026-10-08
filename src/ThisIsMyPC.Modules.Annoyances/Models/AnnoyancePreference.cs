using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Modules.Annoyances.Models;

/// <summary>
/// One suppressible annoyance. Toggle semantics: enabled = the annoyance is suppressed
/// (<see cref="SuppressedValue"/> written); disabled = Windows default behavior.
/// Bit-field preferences use those values as directions and preserve unrelated options.
/// </summary>
public sealed record AnnoyancePreference(
    string Id,
    string DisplayName,
    string Description,
    AnnoyanceSection Section,
    string RegistryKeyPath,
    string RegistryValueName,
    ChangeValueType ValueType,
    string CurrentValue,
    string SuppressedValue,
    string DefaultValue,
    bool IsSuppressed,
    RestartRequirement RestartRequirement)
{
    /// <summary>Only these bits may change. The preset values still express the toggle direction.</summary>
    public uint? ToggleBitMask { get; init; }

    /// <summary>A missing, unreadable, or invalid snapshot prevents a safe change.</summary>
    public string? UnavailableReason { get; init; }
}
