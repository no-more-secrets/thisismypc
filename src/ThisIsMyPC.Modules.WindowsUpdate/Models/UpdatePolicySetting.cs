using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Modules.WindowsUpdate.Models;

/// <summary>
/// One Windows Update policy value. Toggle semantics: configured = <see cref="ConfiguredValue"/>
/// written to the policy hive; not configured = the value is ABSENT (empty
/// <see cref="CurrentValue"/>); restoring deletes the value rather than writing a default,
/// because policy defaults are "unconfigured", not zero.
/// </summary>
public sealed record UpdatePolicySetting(
    string Id,
    string DisplayName,
    string Description,
    string RegistryKeyPath,
    string RegistryValueName,
    ChangeValueType ValueType,
    string CurrentValue,
    string ConfiguredValue)
{
    public bool IsConfigured => CurrentValue.Length > 0
        && string.Equals(CurrentValue, ConfiguredValue, StringComparison.Ordinal);
}
