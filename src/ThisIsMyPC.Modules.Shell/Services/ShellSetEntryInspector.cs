using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.Modules.Shell.Services;

/// <summary>
/// Resolves set entries targeting "Explorer" to live system state: the four taskbar/CLSID
/// toggles via TaskbarSettingsReader (display strings mirror TaskbarChangeFactory) and the
/// Explorer preferences via ExplorerSettingsReader.
/// </summary>
public sealed class ShellSetEntryInspector : ISetEntryInspector
{
    private readonly ExplorerSettingsReader _explorerReader;
    private readonly TaskbarSettingsReader _taskbarReader;

    public ShellSetEntryInspector(IRegistryService registryService)
    {
        _explorerReader = new ExplorerSettingsReader(registryService);
        _taskbarReader = new TaskbarSettingsReader(registryService);
    }

    public string ModuleId => "Explorer";

    public SetEntryState? Inspect(SetEntry entry)
    {
        switch (entry.SettingId)
        {
            case "taskbar-alignment":
            {
                var taskbar = _taskbarReader.Read();
                return Resolve("Taskbar alignment", taskbar.Alignment.ToString(),
                    taskbar.Alignment == 0 ? "Left" : "Center", entry);
            }
            case "taskbar-widgets":
            {
                var taskbar = _taskbarReader.Read();
                return Resolve("Taskbar widgets", taskbar.WidgetsEnabled ? "1" : "0",
                    taskbar.WidgetsEnabled ? "Shown" : "Hidden", entry);
            }
            case "classic-context-menu":
            {
                var taskbar = _taskbarReader.Read();
                return Resolve("Classic context menu",
                    taskbar.ClassicContextMenu ? "" : ShellRegistryPaths.AbsentValue,
                    taskbar.ClassicContextMenu ? "Enabled" : "Disabled", entry);
            }
            case "classic-command-bar":
            {
                var taskbar = _taskbarReader.Read();
                return Resolve("Classic command bar",
                    taskbar.ClassicCommandBar ? "" : ShellRegistryPaths.AbsentValue,
                    taskbar.ClassicCommandBar ? "Classic ribbon" : "Modern toolbar", entry);
            }
        }

        var pref = _explorerReader.ReadAll().FirstOrDefault(p => p.Id == entry.SettingId);
        if (pref is null)
            return null;

        return Resolve(pref.DisplayName, pref.CurrentValue,
            pref.IsEnabled ? "Enabled" : "Disabled", entry);
    }

    private static SetEntryState Resolve(
        string displayName, string currentValue, string currentDisplay, SetEntry entry)
        => new()
        {
            SettingDisplayName = displayName,
            CurrentValue = currentValue,
            CurrentDisplay = currentDisplay,
            IsApplied = string.Equals(currentValue, entry.Value, StringComparison.Ordinal),
        };
}
