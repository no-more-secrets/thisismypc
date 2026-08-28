using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Read-only presentation of discovered startup entries, grouped by source
/// type. Enable/disable toggling arrives in Story 3.2.
/// </summary>
public sealed partial class StartupViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isRegistryViewMode;

    public StartupViewModel(StartupScanData scanData)
    {
        RegistryEntries = new ObservableCollection<StartupEntryItemViewModel>(
            scanData.StartupEntries
                .Where(e => e.Source is StartupSource.RegistryMachineRun or StartupSource.RegistryMachineRunWow64 or StartupSource.RegistryUserRun)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new StartupEntryItemViewModel(e)));
        FolderEntries = new ObservableCollection<StartupEntryItemViewModel>(
            scanData.StartupEntries
                .Where(e => e.Source is StartupSource.StartupFolderUser or StartupSource.StartupFolderCommon)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new StartupEntryItemViewModel(e)));
        TaskEntries = new ObservableCollection<StartupEntryItemViewModel>(
            scanData.StartupEntries
                .Where(e => e.Source == StartupSource.ScheduledTask)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new StartupEntryItemViewModel(e)));
    }

    public ObservableCollection<StartupEntryItemViewModel> RegistryEntries { get; }
    public ObservableCollection<StartupEntryItemViewModel> FolderEntries { get; }
    public ObservableCollection<StartupEntryItemViewModel> TaskEntries { get; }

    public string RegistryHeader => $"Registry ({RegistryEntries.Count})";
    public string FolderHeader => $"Startup Folder ({FolderEntries.Count})";
    public string TaskHeader => $"Scheduled Tasks ({TaskEntries.Count})";

    public bool HasRegistryEntries => RegistryEntries.Count > 0;
    public bool HasFolderEntries => FolderEntries.Count > 0;
    public bool HasTaskEntries => TaskEntries.Count > 0;

    partial void OnIsRegistryViewModeChanged(bool value)
    {
        foreach (var item in RegistryEntries.Concat(FolderEntries).Concat(TaskEntries))
            item.IsRegistryViewMode = value;
    }
}

public sealed partial class StartupEntryItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isRegistryViewMode;

    public StartupEntryItemViewModel(StartupEntry entry)
    {
        Entry = entry;
    }

    public StartupEntry Entry { get; }

    public string Name => Entry.Name;
    public string PublisherText => Entry.Publisher ?? "Unknown publisher";
    public string DescriptionText => Entry.Description ?? string.Empty;
    public bool HasDescription => !string.IsNullOrEmpty(Entry.Description);
    public bool IsEnabled => Entry.IsEnabled;
    public string StateText => Entry.IsEnabled ? "Enabled" : "Disabled";

    /// <summary>Simplified view: the executable that runs (fallback to the raw command).</summary>
    public string FileLocationText => Entry.ExecutablePath ?? Entry.Command;

    /// <summary>Registry view: exact registry key / folder / task path plus the raw command.</summary>
    public string RegistryLocationText => $@"{Entry.SourceLocation}\{Entry.Name}";

    public string SourceLabel => Entry.Source switch
    {
        StartupSource.RegistryMachineRun => "Registry (all users)",
        StartupSource.RegistryMachineRunWow64 => "Registry (all users, 32-bit)",
        StartupSource.RegistryUserRun => "Registry (current user)",
        StartupSource.StartupFolderUser => "Startup folder (current user)",
        StartupSource.StartupFolderCommon => "Startup folder (all users)",
        StartupSource.ScheduledTask => "Scheduled task",
        _ => "Unknown",
    };
}
