using System.Collections.ObjectModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.ViewModels;

public partial class ShellViewModel : ViewModelBase
{
    public ObservableCollection<ShellSettingViewModel> ExplorerSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> TaskbarSettings { get; } = [];

    public ShellViewModel(
        ShellScanData scanData,
        IPendingChangesService pendingChangesService)
    {
        // Explorer preferences
        foreach (var pref in scanData.ExplorerPreferences)
        {
            ExplorerSettings.Add(new ShellSettingViewModel(pref, pendingChangesService));
        }

        // Taskbar settings
        var taskbar = scanData.Taskbar;

        TaskbarSettings.Add(new ShellSettingViewModel(
            label: "Taskbar alignment (Left)",
            description: "Align taskbar icons to the left instead of center",
            systemPath: @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarAl",
            isEnabled: taskbar.Alignment == 0,
            pendingChangesService: pendingChangesService,
            changeFactory: enable => TaskbarChangeFactory.CreateAlignmentChange(taskbar, enable ? 0 : 1)));

        TaskbarSettings.Add(new ShellSettingViewModel(
            label: "Taskbar widgets",
            description: "Show or hide the Widgets button on the taskbar",
            systemPath: @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDa",
            isEnabled: taskbar.WidgetsEnabled,
            pendingChangesService: pendingChangesService,
            changeFactory: enable => TaskbarChangeFactory.CreateWidgetsToggle(taskbar, enable)));

        TaskbarSettings.Add(new ShellSettingViewModel(
            label: "Classic context menu",
            description: "Use Windows 10-style full context menu instead of the compact Windows 11 menu (requires Explorer restart)",
            systemPath: @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32",
            isEnabled: taskbar.ClassicContextMenu,
            pendingChangesService: pendingChangesService,
            changeFactory: enable => TaskbarChangeFactory.CreateClassicContextMenuToggle(taskbar, enable)));
    }
}
