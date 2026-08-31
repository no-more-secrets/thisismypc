using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Changes;

public static class TaskbarChangeFactory
{
    private const string ModuleId = "Explorer";

    // The {86ca1aa0} InprocServer32 override exploits undocumented Explorer internals;
    // informational only; no companion actions.
    private static readonly SettingEnforcement ShimOverrideEnforcement = new()
    {
        ReversionVectors = ["Windows feature updates may restore the modern behavior (undocumented Explorer shim override)"],
    };

    public static ChangeDescriptor CreateAlignmentChange(TaskbarSettings current, int newAlignment)
    {
        if (newAlignment is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(newAlignment), newAlignment, "Taskbar alignment must be 0 (Left) or 1 (Center).");

        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = "taskbar-alignment",
            DisplayName = "Taskbar alignment",
            SystemLocation = $@"{ShellRegistryPaths.AdvancedKeyPath}\TaskbarAl",
            BeforeValue = current.Alignment.ToString(),
            AfterValue = newAlignment.ToString(),
            BeforeDisplay = current.Alignment == 0 ? "Left" : "Center",
            AfterDisplay = newAlignment == 0 ? "Left" : "Center",
            ValueType = ChangeValueType.Registry_DWord,
            Category = ChangeCategory.Modify,
        };
    }

    /// <summary>Win11 SearchboxTaskbarMode display names by value.</summary>
    public static readonly IReadOnlyDictionary<int, string> SearchboxModeNames =
        new Dictionary<int, string>
        {
            [0] = "Hidden",
            [1] = "Icon only",
            [2] = "Icon and label",
            [3] = "Search box",
        };

    /// <summary>TaskbarGlomLevel display names by value.</summary>
    public static readonly IReadOnlyDictionary<int, string> ButtonCombiningNames =
        new Dictionary<int, string>
        {
            [0] = "Always, hide labels",
            [1] = "When taskbar is full",
            [2] = "Never",
        };

    public static ChangeDescriptor CreateSearchboxModeChange(TaskbarSettings current, int newMode)
    {
        if (!SearchboxModeNames.ContainsKey(newMode))
            throw new ArgumentOutOfRangeException(nameof(newMode), newMode, "Taskbar search mode must be 0-3.");

        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = "taskbar-search-mode",
            DisplayName = "Taskbar search",
            SystemLocation = $@"{ShellRegistryPaths.SearchKeyPath}\SearchboxTaskbarMode",
            BeforeValue = current.SearchboxMode.ToString(),
            AfterValue = newMode.ToString(),
            BeforeDisplay = SearchboxModeNames.GetValueOrDefault(current.SearchboxMode, current.SearchboxMode.ToString()),
            AfterDisplay = SearchboxModeNames[newMode],
            ValueType = ChangeValueType.Registry_DWord,
            Category = ChangeCategory.Modify,
            RestartRequirement = RestartRequirement.ExplorerRestart,
        };
    }

    public static ChangeDescriptor CreateButtonCombiningChange(TaskbarSettings current, int newLevel)
    {
        if (!ButtonCombiningNames.ContainsKey(newLevel))
            throw new ArgumentOutOfRangeException(nameof(newLevel), newLevel, "Taskbar button combining must be 0-2.");

        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = "taskbar-button-combining",
            DisplayName = "Combine taskbar buttons",
            SystemLocation = $@"{ShellRegistryPaths.AdvancedKeyPath}\TaskbarGlomLevel",
            BeforeValue = current.ButtonCombining.ToString(),
            AfterValue = newLevel.ToString(),
            BeforeDisplay = ButtonCombiningNames.GetValueOrDefault(current.ButtonCombining, current.ButtonCombining.ToString()),
            AfterDisplay = ButtonCombiningNames[newLevel],
            ValueType = ChangeValueType.Registry_DWord,
            Category = ChangeCategory.Modify,
            RestartRequirement = RestartRequirement.ExplorerRestart,
        };
    }

    public static ChangeDescriptor CreateWidgetsToggle(TaskbarSettings current, bool enable)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = "taskbar-widgets",
            DisplayName = "Taskbar widgets",
            SystemLocation = $@"{ShellRegistryPaths.AdvancedKeyPath}\TaskbarDa",
            BeforeValue = current.WidgetsEnabled ? "1" : "0",
            AfterValue = enable ? "1" : "0",
            BeforeDisplay = current.WidgetsEnabled ? "Shown" : "Hidden",
            AfterDisplay = enable ? "Shown" : "Hidden",
            ValueType = ChangeValueType.Registry_DWord,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
        };
    }

    public static ChangeDescriptor CreateClassicContextMenuToggle(TaskbarSettings current, bool enable)
    {
        // Enable = create key with empty string Default value; Disable = delete key
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = "classic-context-menu",
            DisplayName = "Classic context menu",
            SystemLocation = ShellRegistryPaths.ClassicContextMenuKeyPath,
            BeforeValue = current.ClassicContextMenu ? "" : ShellRegistryPaths.AbsentValue,
            AfterValue = enable ? "" : ShellRegistryPaths.AbsentValue,
            BeforeDisplay = current.ClassicContextMenu ? "Enabled" : "Disabled",
            AfterDisplay = enable ? "Enabled" : "Disabled",
            ValueType = ChangeValueType.Registry_String,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            RestartRequirement = RestartRequirement.ExplorerRestart,
            Enforcement = enable ? ShimOverrideEnforcement : null,
        };
    }

    public static ChangeDescriptor CreateCommandBarToggle(TaskbarSettings current, bool enable)
    {
        // Same CLSID InprocServer32 override pattern as classic context menu
        // Enable = create key with empty Default (disable modern command bar → show classic ribbon)
        // Disable = delete key (restore modern command bar)
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = "classic-command-bar",
            DisplayName = "Classic command bar",
            SystemLocation = ShellRegistryPaths.CommandBarKeyPath,
            BeforeValue = current.ClassicCommandBar ? "" : ShellRegistryPaths.AbsentValue,
            AfterValue = enable ? "" : ShellRegistryPaths.AbsentValue,
            BeforeDisplay = current.ClassicCommandBar ? "Classic ribbon" : "Modern toolbar",
            AfterDisplay = enable ? "Classic ribbon" : "Modern toolbar",
            ValueType = ChangeValueType.Registry_String,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            RestartRequirement = RestartRequirement.ExplorerRestart,
            Enforcement = enable ? ShimOverrideEnforcement : null,
        };
    }
}
