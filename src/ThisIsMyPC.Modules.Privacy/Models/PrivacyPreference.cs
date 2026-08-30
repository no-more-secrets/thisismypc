using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Modules.Privacy.Models;

/// <summary>
/// One privacy setting value. <see cref="ConfiguredValue"/> is the privacy-hardened
/// value; <see cref="DefaultValue"/> is what restore writes — empty string means the
/// Windows default is "no value at all" and restore deletes it (the WU/Power
/// convention). An absent registry value scans as <see cref="DefaultValue"/>.
/// </summary>
public sealed record PrivacyPreference(
    string Id,
    string DisplayName,
    string Description,
    PrivacySection Section,
    string RegistryKeyPath,
    string RegistryValueName,
    ChangeValueType ValueType,
    string CurrentValue,
    string ConfiguredValue,
    string DefaultValue)
{
    public bool IsConfigured => CurrentValue == ConfiguredValue;
}
