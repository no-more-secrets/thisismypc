using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

public sealed class ExplorerSettingsReader
{

    private readonly IRegistryService _registryService;

    public ExplorerSettingsReader(IRegistryService registryService)
    {
        _registryService = registryService;
    }

    public IReadOnlyList<ExplorerPreference> ReadAll()
    {
        var preferences = new List<ExplorerPreference>();

        // Hidden files: Hidden=1 shows, Hidden=2 hides
        preferences.Add(ReadPreference(
            id: "hidden-files",
            displayName: "Show hidden files and folders",
            description: "Display files and folders that are normally hidden",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "Hidden",
            enabledValue: "1",
            disabledValue: "2",
            defaultValue: "2",
            restart: RestartRequirement.ExplorerRefresh));

        // File extensions: HideFileExt=0 shows, HideFileExt=1 hides
        preferences.Add(ReadPreference(
            id: "file-extensions",
            displayName: "Show file name extensions",
            description: "Display file extensions (e.g., .txt, .exe) in Explorer",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "HideFileExt",
            enabledValue: "0",
            disabledValue: "1",
            defaultValue: "1",
            restart: RestartRequirement.ExplorerRefresh));

        // Protected OS files: ShowSuperHidden=1 shows, ShowSuperHidden=0 hides
        preferences.Add(ReadPreference(
            id: "protected-os-files",
            displayName: "Show protected operating system files",
            description: "Display hidden OS files (caution: modifying these can break Windows)",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "ShowSuperHidden",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "0",
            restart: RestartRequirement.ExplorerRefresh));

        // Separate process: SeparateProcess=1 yes, SeparateProcess=0 no
        preferences.Add(ReadPreference(
            id: "separate-process",
            displayName: "Launch folder windows in a separate process",
            description: "Run each Explorer folder in its own process for stability",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "SeparateProcess",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "0",
            restart: RestartRequirement.ExplorerRestart));

        // Sync provider notifications: ShowSyncProviderNotifications=0 off, 1 on
        preferences.Add(ReadPreference(
            id: "sync-provider-notifications",
            displayName: "Show sync provider notifications",
            description: "Display notifications from cloud sync providers like OneDrive in Explorer",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "ShowSyncProviderNotifications",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "1",
            restart: RestartRequirement.ExplorerRefresh));

        // Launch Explorer to: LaunchTo 1=This PC, 2=Quick Access, 3=Home
        preferences.Add(ReadPreference(
            id: "launch-to",
            displayName: "Open Explorer to This PC",
            description: "Launch Explorer to 'This PC' instead of Home/Quick Access",
            keyPath: ShellRegistryPaths.ExplorerKeyPath,
            valueName: "LaunchTo",
            enabledValue: "1",
            disabledValue: "2",
            defaultValue: "2",
            restart: RestartRequirement.None));

        // Navigation pane: show all folders
        preferences.Add(ReadPreference(
            id: "nav-pane-show-all-folders",
            displayName: "Show all folders in navigation pane",
            description: "Display all folders including Control Panel and Recycle Bin in the navigation pane",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "NavPaneShowAllFolders",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "0",
            restart: RestartRequirement.ExplorerRestart));

        // Navigation pane: expand to current folder
        preferences.Add(ReadPreference(
            id: "nav-pane-expand-to-current",
            displayName: "Expand navigation pane to current folder",
            description: "Automatically expand the navigation tree to show the current folder location",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "NavPaneExpandToCurrentFolder",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "0",
            restart: RestartRequirement.ExplorerRestart));

        // Compact view: UseCompactMode=1 compact spacing, 0 normal spacing
        preferences.Add(ReadPreference(
            id: "compact-view",
            displayName: "Use compact view in Explorer",
            description: "Reduce spacing between items in Explorer for a denser file list",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "UseCompactMode",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "0",
            restart: RestartRequirement.None));

        // Item checkboxes: AutoCheckSelect=1 shows selection checkboxes
        preferences.Add(ReadPreference(
            id: "item-checkboxes",
            displayName: "Show item selection checkboxes",
            description: "Display a checkbox next to each item for mouse-driven multi-select",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "AutoCheckSelect",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "0",
            restart: RestartRequirement.ExplorerRefresh));

        // Quick Access recent files: ShowRecent=1 shows
        preferences.Add(ReadPreference(
            id: "quick-access-recent-files",
            displayName: "Show recent files in Quick Access",
            description: "List recently opened files under Quick Access and Home",
            keyPath: ShellRegistryPaths.ExplorerKeyPath,
            valueName: "ShowRecent",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "1",
            restart: RestartRequirement.None));

        // Quick Access frequent folders: ShowFrequent=1 shows
        preferences.Add(ReadPreference(
            id: "quick-access-frequent-folders",
            displayName: "Show frequent folders in Quick Access",
            description: "List frequently used folders under Quick Access and Home",
            keyPath: ShellRegistryPaths.ExplorerKeyPath,
            valueName: "ShowFrequent",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "1",
            restart: RestartRequirement.None));

        // File transfer dialog: EnthusiastMode=1 opens expanded with the speed graph
        preferences.Add(ReadPreference(
            id: "transfer-dialog-details",
            displayName: "Open file transfer dialog in detailed mode",
            description: "Show the copy/move dialog expanded with the transfer speed graph by default",
            keyPath: ShellRegistryPaths.OperationStatusManagerKeyPath,
            valueName: "EnthusiastMode",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "0",
            restart: RestartRequirement.None));

        // Merge conflicts: HideMergeConflicts=0 shows the conflict details
        preferences.Add(ReadPreference(
            id: "merge-conflicts",
            displayName: "Show folder merge conflicts",
            description: "Show details when merging folders that contain items with the same name",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "HideMergeConflicts",
            enabledValue: "0",
            disabledValue: "1",
            defaultValue: "1",
            restart: RestartRequirement.None));

        // Restore folder windows: PersistBrowsers=1 restores at logon
        preferences.Add(ReadPreference(
            id: "restore-folder-windows",
            displayName: "Restore previous folder windows at logon",
            description: "Reopen the Explorer windows you had open when you sign back in",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "PersistBrowsers",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "0",
            restart: RestartRequirement.None));

        // Taskbar clock seconds: ShowSecondsInSystemClock=1 shows seconds
        preferences.Add(ReadPreference(
            id: "seconds-in-clock",
            displayName: "Show seconds in the taskbar clock",
            description: "Display seconds in the system tray clock (uses slightly more power)",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "ShowSecondsInSystemClock",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "0",
            restart: RestartRequirement.ExplorerRefresh));

        // Task View button: ShowTaskViewButton=1 shows
        preferences.Add(ReadPreference(
            id: "task-view-button",
            displayName: "Show the Task View button",
            description: "Display the Task View (virtual desktops) button on the taskbar",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "ShowTaskViewButton",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "1",
            restart: RestartRequirement.None));

        // End Task on taskbar right-click: TaskbarEndTask=1 enables
        preferences.Add(ReadPreference(
            id: "taskbar-end-task",
            displayName: "Show End Task in taskbar right-click",
            description: "Add an \"End task\" entry to taskbar app right-click menus to kill hung apps without Task Manager",
            keyPath: ShellRegistryPaths.TaskbarDeveloperSettingsKeyPath,
            valueName: "TaskbarEndTask",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "0",
            restart: RestartRequirement.None));

        // Start recommendations: Start_IrisRecommendations=0 hides the Recommended feed
        preferences.Add(ReadPreference(
            id: "start-recommendations",
            displayName: "Show Start menu recommendations",
            description: "Show recommendations for tips, shortcuts, and new apps in the Start menu's Recommended section (recently opened files are controlled separately)",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "Start_IrisRecommendations",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "1",
            restart: RestartRequirement.None));

        // Start account notifications: Start_AccountNotifications=0 hides account nags
        preferences.Add(ReadPreference(
            id: "start-account-notifications",
            displayName: "Show Microsoft account notifications in Start",
            description: "Show account-related alerts (backup nags, subscription prompts) next to your name in the Start menu",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "Start_AccountNotifications",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "1",
            restart: RestartRequirement.None));

        // Snap Assist: SnapAssist=1 suggests what to snap next to a snapped window
        preferences.Add(ReadPreference(
            id: "snap-assist",
            displayName: "Show Snap Assist suggestions",
            description: "When you snap a window, suggest what to snap next to it",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "SnapAssist",
            enabledValue: "1",
            disabledValue: "0",
            defaultValue: "1",
            restart: RestartRequirement.None));

        // Aero Shake: DisallowShaking=0 lets shaking a title bar minimize other windows
        preferences.Add(ReadPreference(
            id: "aero-shake",
            displayName: "Enable title bar window shake",
            description: "Shake a window's title bar to minimize all other windows (off by default in Windows 11)",
            keyPath: ShellRegistryPaths.AdvancedKeyPath,
            valueName: "DisallowShaking",
            enabledValue: "0",
            disabledValue: "1",
            defaultValue: "1",
            restart: RestartRequirement.None));

        return preferences;
    }

    private ExplorerPreference ReadPreference(
        string id,
        string displayName,
        string description,
        string keyPath,
        string valueName,
        string enabledValue,
        string disabledValue,
        string defaultValue,
        RestartRequirement restart)
    {
        var result = _registryService.ReadDWord(keyPath, valueName);
        var currentValue = result.IsSuccess ? result.Value!.ToString() : defaultValue;

        return new ExplorerPreference(
            Id: id,
            DisplayName: displayName,
            Description: description,
            RegistryKeyPath: keyPath,
            RegistryValueName: valueName,
            ValueType: ChangeValueType.Registry_DWord,
            CurrentValue: currentValue,
            EnabledValue: enabledValue,
            DisabledValue: disabledValue,
            IsEnabled: currentValue == enabledValue,
            RestartRequirement: restart);
    }
}
