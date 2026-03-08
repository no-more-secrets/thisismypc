using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

public sealed class ExplorerSettingsReader
{
    private const string AdvancedKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ExplorerKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer";

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
            keyPath: AdvancedKeyPath,
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
            keyPath: AdvancedKeyPath,
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
            keyPath: AdvancedKeyPath,
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
            keyPath: AdvancedKeyPath,
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
            keyPath: AdvancedKeyPath,
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
            keyPath: ExplorerKeyPath,
            valueName: "LaunchTo",
            enabledValue: "1",
            disabledValue: "2",
            defaultValue: "2",
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
