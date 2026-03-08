using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Changes;

public static class TaskbarChangeFactory
{
    private const string ModuleId = "Shell & Explorer";
    private const string AdvancedKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ClassicContextMenuKeyPath = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

    public static ChangeDescriptor CreateAlignmentChange(TaskbarSettings current, int newAlignment)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = "taskbar-alignment",
            DisplayName = "Taskbar alignment",
            SystemLocation = $@"{AdvancedKeyPath}\TaskbarAl",
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
            SystemLocation = $@"{AdvancedKeyPath}\TaskbarDa",
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
            SystemLocation = ClassicContextMenuKeyPath,
            BeforeValue = current.ClassicContextMenu ? "" : "__absent__",
            AfterValue = enable ? "" : "__absent__",
            BeforeDisplay = current.ClassicContextMenu ? "Enabled" : "Disabled",
            AfterDisplay = enable ? "Enabled" : "Disabled",
            ValueType = ChangeValueType.Registry_String,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
        };
    }
}
