using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.ViewModels;

public partial class ShellViewModel : ViewModelBase, ISearchFocusTarget
{
    private static readonly string AdvancedKeyPath = Modules.Shell.ShellRegistryPaths.AdvancedKeyPath;
    private static readonly string ClassicContextMenuKeyPath = Modules.Shell.ShellRegistryPaths.ClassicContextMenuKeyPath;
    private static readonly string CommandBarKeyPath = Modules.Shell.ShellRegistryPaths.CommandBarKeyPath;

    public ObservableCollection<ShellSettingViewModel> ExplorerSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> TaskbarSettings { get; } = [];
    public ObservableCollection<ShellChoiceSettingViewModel> TaskbarChoiceSettings { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        foreach (var row in ExplorerSettings)
            row.ApplySearch(value);
        foreach (var row in TaskbarSettings)
            row.ApplySearch(value);
        foreach (var row in TaskbarChoiceSettings)
            row.ApplySearch(value);
    }

    public ShellViewModel(
        ShellScanData scanData,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService)
    {
        // Explorer preferences
        foreach (var pref in scanData.ExplorerPreferences)
        {
            var capturedPref = pref;
            ExplorerSettings.Add(new ShellSettingViewModel(
                capturedPref,
                pendingChangesService,
                readRegistryState: () => ReadExplorerPrefFromRegistry(registryService, capturedPref)));
        }

        // Command bar style (Explorer visual, not a DWord preference; CLSID override)
        var taskbar = scanData.Taskbar;
        ExplorerSettings.Add(new ShellSettingViewModel(
            label: "Use classic command bar",
            description: "Show the classic ribbon/command bar instead of the modern Windows 11 toolbar in File Explorer (requires Explorer restart)",
            systemPath: CommandBarKeyPath,
            isEnabled: taskbar.ClassicCommandBar,
            pendingChangesService: pendingChangesService,
            changeFactory: enable => TaskbarChangeFactory.CreateCommandBarToggle(taskbar, enable),
            readRegistryState: () =>
            {
                var result = registryService.KeyExists(CommandBarKeyPath);
                return result.IsSuccess && result.Value;
            }));

        // Taskbar settings

        TaskbarSettings.Add(new ShellSettingViewModel(
            label: "Taskbar alignment (Left)",
            description: "Align taskbar icons to the left instead of center",
            systemPath: $@"{AdvancedKeyPath}\TaskbarAl",
            isEnabled: taskbar.Alignment == 0,
            pendingChangesService: pendingChangesService,
            changeFactory: enable => TaskbarChangeFactory.CreateAlignmentChange(taskbar, enable ? 0 : 1),
            readRegistryState: () =>
            {
                var result = registryService.ReadDWord(AdvancedKeyPath, "TaskbarAl");
                return result.IsSuccess && result.Value == 0;
            }));

        TaskbarSettings.Add(new ShellSettingViewModel(
            label: "Taskbar widgets",
            description: "Show or hide the Widgets button on the taskbar",
            systemPath: $@"{AdvancedKeyPath}\TaskbarDa",
            isEnabled: taskbar.WidgetsEnabled,
            pendingChangesService: pendingChangesService,
            changeFactory: enable => TaskbarChangeFactory.CreateWidgetsToggle(taskbar, enable),
            readRegistryState: () =>
            {
                var result = registryService.ReadDWord(AdvancedKeyPath, "TaskbarDa");
                return result.IsSuccess && result.Value == 1;
            }));

        TaskbarChoiceSettings.Add(new ShellChoiceSettingViewModel(
            label: "Taskbar search",
            description: "How search appears on the taskbar (takes effect after Explorer restarts)",
            systemPath: $@"{Modules.Shell.ShellRegistryPaths.SearchKeyPath}\SearchboxTaskbarMode",
            options: TaskbarChangeFactory.SearchboxModeNames
                .OrderBy(p => p.Key)
                .Select(p => new ShellChoiceOption(p.Key, p.Value))
                .ToList(),
            currentValue: taskbar.SearchboxMode,
            pendingChangesService: pendingChangesService,
            changeFactory: mode => TaskbarChangeFactory.CreateSearchboxModeChange(taskbar, mode),
            readRegistryValue: () =>
            {
                var result = registryService.ReadDWord(Modules.Shell.ShellRegistryPaths.SearchKeyPath, "SearchboxTaskbarMode");
                return result.IsSuccess ? result.Value! : 3;
            }));

        TaskbarChoiceSettings.Add(new ShellChoiceSettingViewModel(
            label: "Combine taskbar buttons",
            description: "When windows of the same app share one taskbar button (takes effect after Explorer restarts)",
            systemPath: $@"{AdvancedKeyPath}\TaskbarGlomLevel",
            options: TaskbarChangeFactory.ButtonCombiningNames
                .OrderBy(p => p.Key)
                .Select(p => new ShellChoiceOption(p.Key, p.Value))
                .ToList(),
            currentValue: taskbar.ButtonCombining,
            pendingChangesService: pendingChangesService,
            changeFactory: level => TaskbarChangeFactory.CreateButtonCombiningChange(taskbar, level),
            readRegistryValue: () =>
            {
                var result = registryService.ReadDWord(AdvancedKeyPath, "TaskbarGlomLevel");
                return result.IsSuccess ? result.Value! : 0;
            }));

        TaskbarSettings.Add(new ShellSettingViewModel(
            label: "Classic context menu",
            description: "Use Windows 10-style full context menu instead of the compact Windows 11 menu (requires Explorer restart)",
            systemPath: ClassicContextMenuKeyPath,
            isEnabled: taskbar.ClassicContextMenu,
            pendingChangesService: pendingChangesService,
            changeFactory: enable => TaskbarChangeFactory.CreateClassicContextMenuToggle(taskbar, enable),
            readRegistryState: () =>
            {
                var result = registryService.KeyExists(ClassicContextMenuKeyPath);
                return result.IsSuccess && result.Value;
            }));
    }

    private static bool ReadExplorerPrefFromRegistry(IRegistryService registryService, ExplorerPreference pref)
    {
        if (pref.ValueType == Core.Changes.ChangeValueType.Registry_String)
        {
            // Absent string value = the delete-to-restore state (AbsentValue)
            var read = registryService.ReadString(pref.RegistryKeyPath, pref.RegistryValueName);
            var current = read.IsSuccess ? read.Value! : Modules.Shell.ShellRegistryPaths.AbsentValue;
            return current == pref.EnabledValue;
        }

        var result = registryService.ReadDWord(pref.RegistryKeyPath, pref.RegistryValueName);
        if (!result.IsSuccess)
            return pref.IsEnabled; // fallback to scan value if read fails
        return result.Value.ToString() == pref.EnabledValue;
    }
}

