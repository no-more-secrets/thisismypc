namespace ThisIsMyPC.Modules.Startup.Models;

/// <summary>Autoruns tabs, in Autoruns' order.</summary>
public enum AutorunCategory
{
    Logon,
    Explorer,
    InternetExplorer,
    ScheduledTasks,
    Services,
    Drivers,
    FontDrivers,
    Drivers32,
    KnownDlls,
    Winlogon,
    WinsockProviders,
    PrintMonitors,
    Office,
}

/// <summary>What kind of thing the item is, which decides how it is disabled.</summary>
public enum AutorunItemKind
{
    /// <summary>A named value under a key; disabled by moving it into the key's AutorunsDisabled subkey.</summary>
    RegistryValue,

    /// <summary>A subkey under a key; disabled by moving the whole subkey under AutorunsDisabled.</summary>
    RegistryKey,

    /// <summary>A file in a Startup folder; disabled by moving it into the folder's AutorunsDisabled subfolder.</summary>
    StartupFile,

    /// <summary>A Task Scheduler task; disabled through the scheduler.</summary>
    ScheduledTask,

    /// <summary>A service or driver key; disabled by Start=4 with the old Start kept in an AutorunsDisabled value.</summary>
    Service,
}

/// <summary>
/// One autostart item the way Autoruns lists it. <see cref="Location"/> is
/// where the item lives when enabled (key path, folder, task path, or service
/// key) and <see cref="Name"/> the value, subkey, file, task, or service name
/// under it. Together they name the item to the toggler (see AutorunTarget).
/// </summary>
public sealed record AutorunEntry
{
    public required AutorunCategory Category { get; init; }
    public required AutorunItemKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Location { get; init; }

    /// <summary>The raw registry data, CLSID, DLL name, task path, or ImagePath the item points at.</summary>
    public required string Data { get; init; }

    /// <summary>The file the item runs or loads, when it could be resolved.</summary>
    public string? ImagePath { get; init; }

    public string? Description { get; init; }
    public string? Publisher { get; init; }
    public required bool IsEnabled { get; init; }

    /// <summary>False when <see cref="ImagePath"/> names a file that is not there (Autoruns' yellow rows).</summary>
    public bool FileExists { get; init; } = true;

    /// <summary>The image file's last write time, local.</summary>
    public DateTime? Timestamp { get; init; }

    /// <summary>When the location (registry key or folder) was last written, local; the group header's date.</summary>
    public DateTime? LocationTimestamp { get; init; }

    /// <summary>Extra state worth a glance: "Off in Task Manager", "Boot start", and the like.</summary>
    public string? Note { get; init; }

    /// <summary>
    /// False for items another page of this app owns the state of (a shell
    /// handler switched off on the Context Menus page); the Autoruns switch is
    /// greyed so the two mechanisms never fight over one key.
    /// </summary>
    public bool CanToggle { get; init; } = true;

    /// <summary>
    /// Set when this live item sits beside its parked twin: the program
    /// re-registered itself after the user switched it off. Holds the live
    /// copy (AutorunSnapshot JSON) so switching it off can purge the copy and
    /// undo can put it back. The parked twin is not listed separately.
    /// </summary>
    public string? LiveSnapshot { get; init; }

    public bool IsReRegistered => LiveSnapshot is not null;

    /// <summary>The header every scheduled task sits under; a task's own path is not a location.</summary>
    public const string TaskSchedulerLocation = "Task Scheduler";

    /// <summary>What the page groups rows under: the key or folder, or "Task Scheduler" for every task.</summary>
    public string LocationGroup => Kind == AutorunItemKind.ScheduledTask ? TaskSchedulerLocation : Location;

    public static string CategoryName(AutorunCategory category) => category switch
    {
        AutorunCategory.Logon => "Logon",
        AutorunCategory.Explorer => "Explorer",
        AutorunCategory.InternetExplorer => "Internet Explorer",
        AutorunCategory.ScheduledTasks => "Scheduled Tasks",
        AutorunCategory.Services => "Services",
        AutorunCategory.Drivers => "Drivers",
        AutorunCategory.FontDrivers => "Font Drivers",
        AutorunCategory.Drivers32 => "32-Bit Drivers",
        AutorunCategory.KnownDlls => "Known DLLs",
        AutorunCategory.Winlogon => "Winlogon",
        AutorunCategory.WinsockProviders => "Winsock Providers",
        AutorunCategory.PrintMonitors => "Print Monitors",
        AutorunCategory.Office => "Office",
        _ => category.ToString(),
    };
}
