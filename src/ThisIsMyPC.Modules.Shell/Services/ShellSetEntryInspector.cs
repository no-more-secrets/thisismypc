using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Shell.Changes;

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

    public ChangeGroup? CreateChangeGroup(SetEntry entry)
    {
        switch (entry.SettingId)
        {
            case "taskbar-alignment":
                return entry.Value is "0" or "1"
                    ? Wrap(TaskbarChangeFactory.CreateAlignmentChange(
                        _taskbarReader.Read(), newAlignment: int.Parse(entry.Value, System.Globalization.CultureInfo.InvariantCulture)))
                    : null;
            case "taskbar-widgets":
                return entry.Value is "0" or "1"
                    ? Wrap(TaskbarChangeFactory.CreateWidgetsToggle(_taskbarReader.Read(), enable: entry.Value == "1"))
                    : null;
            case "classic-context-menu":
                return entry.Value == "" || entry.Value == ShellRegistryPaths.AbsentValue
                    ? Wrap(TaskbarChangeFactory.CreateClassicContextMenuToggle(_taskbarReader.Read(), enable: entry.Value == ""))
                    : null;
            case "classic-command-bar":
                return entry.Value == "" || entry.Value == ShellRegistryPaths.AbsentValue
                    ? Wrap(TaskbarChangeFactory.CreateCommandBarToggle(_taskbarReader.Read(), enable: entry.Value == ""))
                    : null;
        }

        var pref = _explorerReader.ReadAll().FirstOrDefault(p => p.Id == entry.SettingId);
        if (pref is null)
            return null;

        var enable = string.Equals(entry.Value, pref.EnabledValue, StringComparison.Ordinal) ? true
            : string.Equals(entry.Value, pref.DisabledValue, StringComparison.Ordinal) ? false
            : (bool?)null;
        return enable is { } direction
            ? Wrap(ExplorerChangeFactory.CreateToggle(pref, direction), pref.Description)
            : null;
    }

    private static ChangeGroup Wrap(ChangeDescriptor change, string? description = null) => new()
    {
        GroupId = Guid.NewGuid().ToString("N"),
        DisplayName = change.DisplayName,
        Description = description ?? change.DisplayName,
        Changes = [change],
    };

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
