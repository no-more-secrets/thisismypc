namespace ThisIsMyPC.Modules.Startup.Models;

public enum StartupSource
{
    RegistryMachineRun,
    RegistryMachineRunWow64,
    RegistryUserRun,
    StartupFolderUser,
    StartupFolderCommon,
    ScheduledTask,
}

public sealed record StartupEntry
{
    /// <summary>Registry value name, shortcut file name, or task name.</summary>
    public required string Name { get; init; }

    /// <summary>Raw command line (registry value) or file path (startup folder item).</summary>
    public required string Command { get; init; }

    /// <summary>Full path to the executable, parsed from the command or resolved from the shortcut. Null when unparseable.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Company name from the executable's version info. Null when the file is missing or unstamped.</summary>
    public string? Publisher { get; init; }

    /// <summary>File description from the executable's version info.</summary>
    public string? Description { get; init; }

    public required StartupSource Source { get; init; }

    /// <summary>Exact registry key path, startup folder path, or task scheduler path the entry came from.</summary>
    public required string SourceLocation { get; init; }

    /// <summary>False when Windows' StartupApproved state marks the entry disabled.</summary>
    public required bool IsEnabled { get; init; }
}
