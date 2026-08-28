using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Changes;

/// <summary>Builds ChangeDescriptors that enable/disable scheduled tasks via ITaskService.</summary>
public static class ScheduledTaskChangeFactory
{
    private const string ModuleId = "Startup & Services";

    public static string GetSettingId(string taskPath) => $"scheduled-task:{taskPath}";

    public static ChangeDescriptor CreateToggle(ScheduledTaskEntry entry, bool enable)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = GetSettingId(entry.Path),
            DisplayName = $"Scheduled task: {entry.Name}",
            SystemLocation = entry.Path,
            BeforeValue = entry.IsEnabled ? "Enabled" : "Disabled",
            AfterValue = enable ? "Enabled" : "Disabled",
            BeforeDisplay = entry.IsEnabled ? "Enabled" : "Disabled",
            AfterDisplay = enable ? "Enabled" : "Disabled",
            ValueType = ChangeValueType.ScheduledTask_State,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            RestartRequirement = RestartRequirement.None,
        };
    }
}
