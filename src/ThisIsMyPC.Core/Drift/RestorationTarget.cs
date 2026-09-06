using System.Collections.Immutable;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Drift;

/// <summary>
/// One exact registry value Owner Mode is allowed to restore: the module and
/// setting that own it, the exact key and value name, the value type, and the
/// closed set of values restoration may write. Anything not listed here is
/// not restorable, whatever the baseline says. Entries are trusted product
/// metadata copied from the owning module's reader and change factory; the
/// <see cref="Provenance"/> line names that source so a later parity test can
/// check it.
/// </summary>
public sealed record RestorationTarget
{
    public required string ModuleId { get; init; }
    public required string SettingId { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Key path with an HKCU root, exactly as the module writes it.</summary>
    public required string KeyPath { get; init; }
    public required string ValueName { get; init; }
    public required ChangeValueType ValueType { get; init; }

    /// <summary>The value the module writes to suppress the annoyance.</summary>
    public required RegistryValueData SuppressedValue { get; init; }

    /// <summary>The value the module writes to restore Windows behavior (an explicit write, never a delete).</summary>
    public required RegistryValueData WindowsDefaultValue { get; init; }

    /// <summary>Every value restoration may ever write for this target; nothing else is accepted.</summary>
    public required ImmutableArray<RegistryValueData> AllowedDesiredValues { get; init; }

    /// <summary>Where the values were read from (type and member), for review and parity checks.</summary>
    public required string Provenance { get; init; }

    /// <summary>True when the key lives in a user profile hive and needs a SID to resolve.</summary>
    public bool IsUserHive => KeyPath.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A single DWORD on/off target whose module writes <paramref name="suppressed"/>
    /// to suppress and <paramref name="windowsDefault"/> to restore.
    /// </summary>
    public static RestorationTarget DWordToggle(
        string moduleId, string settingId, string displayName,
        string keyPath, string valueName, int suppressed, int windowsDefault, string provenance)
    {
        var suppressedValue = RegistryValueData.FromDWord(suppressed);
        var defaultValue = RegistryValueData.FromDWord(windowsDefault);
        return new RestorationTarget
        {
            ModuleId = moduleId,
            SettingId = settingId,
            DisplayName = displayName,
            KeyPath = keyPath,
            ValueName = valueName,
            ValueType = ChangeValueType.Registry_DWord,
            SuppressedValue = suppressedValue,
            WindowsDefaultValue = defaultValue,
            AllowedDesiredValues = [suppressedValue, defaultValue],
            Provenance = provenance,
        };
    }
}
