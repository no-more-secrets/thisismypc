using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Modules.Annoyances.Models;

/// <summary>
/// One suppressible annoyance. Toggle semantics: enabled = the annoyance is suppressed
/// (<see cref="SuppressedValue"/> written); disabled = Windows default behavior.
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
    RestartRequirement RestartRequirement);
