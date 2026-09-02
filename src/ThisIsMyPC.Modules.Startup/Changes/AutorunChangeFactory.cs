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

    public static string GetSettingId(AutorunEntry entry) => SettingIdPrefix + AutorunTarget.For(entry).Encode();

    public static ChangeDescriptor CreateToggle(AutorunEntry entry, bool enable)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = GetSettingId(entry),
            DisplayName = $"{AutorunEntry.CategoryName(entry.Category)}: {entry.Name}",
            SystemLocation = AutorunTarget.For(entry).Encode(),
            BeforeValue = entry.IsEnabled ? EnabledValue : DisabledValue,
            AfterValue = enable ? EnabledValue : DisabledValue,
            BeforeDisplay = entry.IsEnabled ? EnabledValue : DisabledValue,
            AfterDisplay = enable ? EnabledValue : DisabledValue,
            ValueType = ChangeValueType.Autorun_State,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            RestartRequirement = RestartFor(entry.Category),
        };
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
