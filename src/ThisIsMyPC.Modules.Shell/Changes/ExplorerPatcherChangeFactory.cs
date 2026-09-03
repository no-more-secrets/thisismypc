using System.Globalization;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Changes;

/// <summary>
/// Builds reversible changes for settings imported from ExplorerPatcher.
/// They are ordinary registry DWORD writes, so ShellModule applies them with
/// no special case. A value that was absent is recorded as absent, and undo
/// deletes it again rather than writing ExplorerPatcher's default over it.
/// </summary>
public static class ExplorerPatcherChangeFactory
{
    public const string ModuleId = "Explorer";

    /// <summary>SettingId prefix, so a staged row is recognisable across visits.</summary>
    public const string SettingIdPrefix = "explorerpatcher:";

    /// <summary>
    /// One setting moved to <paramref name="newValue"/>.
    /// <paramref name="liveValue"/> is the value read now, or null when absent.
    /// </summary>
    public static ChangeDescriptor Create(ExplorerPatcherSetting setting, int? liveValue, int newValue)
    {
        ArgumentNullException.ThrowIfNull(setting);

        var before = liveValue?.ToString(CultureInfo.InvariantCulture) ?? ShellRegistryPaths.AbsentValue;
        var beforeDisplay = liveValue is { } live
            ? setting.DisplayFor(live)
            : $"{setting.DisplayFor(setting.DefaultValue)} (not set)";

        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = SettingIdPrefix + setting.RegistryValueName,
            DisplayName = setting.DisplayName,
            SystemLocation = setting.SystemLocation,
            BeforeValue = before,
            AfterValue = newValue.ToString(CultureInfo.InvariantCulture),
            BeforeDisplay = beforeDisplay,
            AfterDisplay = setting.DisplayFor(newValue),
            ValueType = ChangeValueType.Registry_DWord,
            Category = CategoryFor(setting, newValue),
            RestartRequirement = setting.RequiresExplorerRestart
                ? RestartRequirement.ExplorerRestart
                : RestartRequirement.None,
        };
    }

    private static ChangeCategory CategoryFor(ExplorerPatcherSetting setting, int newValue)
    {
        if (setting.Kind == ExplorerPatcherSettingKind.Choice)
            return ChangeCategory.Modify;
        var on = setting.Kind == ExplorerPatcherSettingKind.InvertedToggle ? newValue == 0 : newValue != 0;
        return on ? ChangeCategory.Enable : ChangeCategory.Disable;
    }
}
