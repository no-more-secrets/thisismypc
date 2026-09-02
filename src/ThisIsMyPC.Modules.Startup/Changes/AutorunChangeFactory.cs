using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Startup.Models;
using ThisIsMyPC.Modules.Startup.Services;

namespace ThisIsMyPC.Modules.Startup.Changes;

/// <summary>
/// Builds the ChangeDescriptor that enables or disables one Autoruns item.
/// Before/After are the words "Enabled" and "Disabled"; the item itself is
/// named by SystemLocation (AutorunTarget). Undo is the opposite move, and
/// the moved data is never destroyed, so no snapshot is needed.
/// </summary>
public static class AutorunChangeFactory
{
    public const string ModuleId = "Startup & Services";
    public const string SettingIdPrefix = "autorun:";
    public const string EnabledValue = "Enabled";
    public const string DisabledValue = "Disabled";

    /// <summary>Suffix on the id of a row that sits in AutorunsDisabled, so a parked twin never shares an identity with the live item.</summary>
    public const string ParkedSuffix = "|parked";

    public static string GetSettingId(AutorunEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var id = SettingIdPrefix + AutorunTarget.For(entry).Encode();
        return entry.IsEnabled || entry.Kind is AutorunItemKind.ScheduledTask or AutorunItemKind.Service ? id : id + ParkedSuffix;
    }

    /// <summary>Separates the state word from the snapshot JSON of a re-registered live copy.</summary>
    public const char SnapshotSeparator = ';';

    public static ChangeDescriptor CreateToggle(AutorunEntry entry, bool enable)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var before = entry.IsEnabled ? EnabledValue : DisabledValue;
        // A re-registered copy carries its snapshot, so disabling purges it and undo restores it.
        if (entry.IsEnabled && entry.LiveSnapshot is not null)
            before += SnapshotSeparator + entry.LiveSnapshot;
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = GetSettingId(entry),
            DisplayName = entry.IsReRegistered
                ? $"{AutorunEntry.CategoryName(entry.Category)}: {entry.Name} (re-registered copy)"
                : $"{AutorunEntry.CategoryName(entry.Category)}: {entry.Name}",
            SystemLocation = AutorunTarget.For(entry).Encode(),
            BeforeValue = before,
            AfterValue = enable ? EnabledValue : DisabledValue,
            BeforeDisplay = entry.IsEnabled ? EnabledValue : DisabledValue,
            AfterDisplay = enable ? EnabledValue : DisabledValue,
            ValueType = ChangeValueType.Autorun_State,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            RestartRequirement = RestartFor(entry.Category),
        };
    }

    /// <summary>Reads "Enabled", "Disabled", or "Enabled;{snapshot}"; null when the word is neither.</summary>
    public static bool? ParseState(string? value, out AutorunSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrEmpty(value))
            return null;
        var cut = value.IndexOf(SnapshotSeparator, StringComparison.Ordinal);
        var word = cut < 0 ? value : value[..cut];
        if (cut >= 0)
            snapshot = AutorunSnapshot.Deserialize(value[(cut + 1)..]);
        if (string.Equals(word, EnabledValue, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(word, DisabledValue, StringComparison.OrdinalIgnoreCase))
            return false;
        return null;
    }

    /// <summary>When the change takes effect. Explorer reloads its handlers on restart; system-level hooks need a reboot.</summary>
    public static RestartRequirement RestartFor(AutorunCategory category) => category switch
    {
        AutorunCategory.Explorer => RestartRequirement.ExplorerRestart,
        AutorunCategory.Services or AutorunCategory.Drivers or AutorunCategory.FontDrivers or AutorunCategory.Drivers32
            or AutorunCategory.KnownDlls or AutorunCategory.Winlogon or AutorunCategory.WinsockProviders
            or AutorunCategory.PrintMonitors => RestartRequirement.Reboot,
        _ => RestartRequirement.None,
    };
}
