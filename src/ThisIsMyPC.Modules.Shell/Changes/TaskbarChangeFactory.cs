using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Changes;

public static class TaskbarChangeFactory
{
    private const string ModuleId = "Explorer";

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
        };
    }
}
