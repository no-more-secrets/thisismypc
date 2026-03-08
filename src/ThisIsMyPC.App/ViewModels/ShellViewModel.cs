using System.Collections.ObjectModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.ViewModels;

public partial class ShellViewModel : ViewModelBase
{
    private const string AdvancedKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ClassicContextMenuClsidKeyPath = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

    public ObservableCollection<ShellSettingViewModel> ExplorerSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> TaskbarSettings { get; } = [];

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

        // Taskbar settings
        var taskbar = scanData.Taskbar;

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

        TaskbarSettings.Add(new ShellSettingViewModel(
            label: "Classic context menu",
            description: "Use Windows 10-style full context menu instead of the compact Windows 11 menu (requires Explorer restart)",
            systemPath: $@"{ClassicContextMenuClsidKeyPath}\InprocServer32",
            isEnabled: taskbar.ClassicContextMenu,
            pendingChangesService: pendingChangesService,
            changeFactory: enable => TaskbarChangeFactory.CreateClassicContextMenuToggle(taskbar, enable),
            readRegistryState: () =>
            {
                var result = registryService.KeyExists($@"{ClassicContextMenuClsidKeyPath}\InprocServer32");
                return result.IsSuccess && result.Value;
            }));
    }

    private static bool ReadExplorerPrefFromRegistry(IRegistryService registryService, ExplorerPreference pref)
    {
        var result = registryService.ReadDWord(pref.RegistryKeyPath, pref.RegistryValueName);
        if (!result.IsSuccess)
            return pref.IsEnabled; // fallback to scan value if read fails
        return result.Value.ToString() == pref.EnabledValue;
    }
}
