using System.Diagnostics;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Services;

/// <summary>Version-info metadata for an executable on disk.</summary>
public sealed record StartupFileMetadata(string? Publisher, string? Description);

/// <summary>
/// Discovers startup entries from registry Run keys and startup folders,
/// reading enabled/disabled state from Windows' StartupApproved keys
/// (the same mechanism Task Manager uses).
/// </summary>
public sealed class StartupScanner
{
    public const string MachineRunKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    public const string MachineRunWow64Key = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    public const string UserRunKey = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public const string MachineApprovedRunKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string MachineApprovedRun32Key = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    public const string UserApprovedRunKey = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string UserApprovedStartupFolderKey = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    public const string MachineApprovedStartupFolderKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    private readonly IRegistryService _registry;
    private readonly IStartupFolderService _startupFolders;
    private readonly Func<string, StartupFileMetadata> _fileMetadataReader;
    private readonly Dictionary<string, StartupFileMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly Func<IReadOnlyList<StartupEntry>>? _scheduledTaskSource;

    public StartupScanner(
        IRegistryService registry,
        IStartupFolderService startupFolders,
        Func<string, StartupFileMetadata>? fileMetadataReader = null,
        Func<IReadOnlyList<StartupEntry>>? scheduledTaskSource = null)
    {
        _registry = registry;
        _startupFolders = startupFolders;
        _fileMetadataReader = fileMetadataReader ?? ReadFileMetadata;
        _scheduledTaskSource = scheduledTaskSource;
    }

    public StartupScanData Scan()
    {
        var entries = new List<StartupEntry>();
        ScanRunKey(entries, MachineRunKey, MachineApprovedRunKey, StartupSource.RegistryMachineRun);
        ScanRunKey(entries, MachineRunWow64Key, MachineApprovedRun32Key, StartupSource.RegistryMachineRunWow64);
        ScanRunKey(entries, UserRunKey, UserApprovedRunKey, StartupSource.RegistryUserRun);
        ScanStartupFolder(entries, StartupFolderScope.CurrentUser, UserApprovedStartupFolderKey, StartupSource.StartupFolderUser);
        ScanStartupFolder(entries, StartupFolderScope.AllUsers, MachineApprovedStartupFolderKey, StartupSource.StartupFolderCommon);

        // Startup-related scheduled tasks plug in here once ITaskService COM
        // interop lands (Story 3.4); until then the group scans empty.
        if (_scheduledTaskSource is not null)
            entries.AddRange(_scheduledTaskSource());

        return new StartupScanData(entries, []);
    }

    private void ScanRunKey(List<StartupEntry> entries, string runKey, string approvedKey, StartupSource source)
    {
        var valueNames = _registry.EnumerateValues(runKey);
        if (!valueNames.IsSuccess || valueNames.Value is null)
            return; // key absent on this machine — nothing to report

        foreach (var name in valueNames.Value)
        {
            if (name.Length == 0)
                continue; // default value is not a startup entry

            var command = _registry.ReadString(runKey, name);
            if (!command.IsSuccess || string.IsNullOrWhiteSpace(command.Value))
                continue;

            var executablePath = ExtractExecutablePath(command.Value);
            var metadata = GetMetadata(executablePath);

            entries.Add(new StartupEntry
            {
                Name = name,
                Command = command.Value,
                ExecutablePath = executablePath,
                Publisher = metadata?.Publisher,
                Description = metadata?.Description,
                Source = source,
                SourceLocation = runKey,
                IsEnabled = ReadApprovedState(approvedKey, name),
            });
        }
    }

    private void ScanStartupFolder(List<StartupEntry> entries, StartupFolderScope scope, string approvedKey, StartupSource source)
    {
        var items = _startupFolders.Enumerate(scope);
        if (!items.IsSuccess || items.Value is null)
            return;

        foreach (var item in items.Value)
        {
            var fileName = Path.GetFileName(item.FilePath);
            var executablePath = item.ResolvedTarget
                ?? (Path.GetExtension(item.FilePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? item.FilePath : null);
            var metadata = GetMetadata(executablePath);

            entries.Add(new StartupEntry
            {
                Name = fileName,
                Command = item.FilePath,
                ExecutablePath = executablePath,
                Publisher = metadata?.Publisher,
                Description = metadata?.Description,
                Source = source,
                SourceLocation = Path.GetDirectoryName(item.FilePath) ?? item.FilePath,
                IsEnabled = ReadApprovedState(approvedKey, fileName),
            });
        }
    }

    /// <summary>
    /// StartupApproved values are 12-byte REG_BINARY blobs: an even first byte
    /// (0x02, 0x06) means enabled, an odd first byte (0x03, 0x07) disabled.
    /// A missing value means the entry has never been toggled — enabled.
    /// </summary>
    private bool ReadApprovedState(string approvedKey, string valueName)
    {
        var state = _registry.ReadBinary(approvedKey, valueName);
        if (!state.IsSuccess || state.Value is null || state.Value.Length == 0)
            return true;
        return (state.Value[0] & 1) == 0;
    }

    /// <summary>
    /// Parses the executable path out of a Run-key command line: quoted paths,
    /// bare paths, and unquoted paths with arguments ("C:\Tools\app.exe -m").
    /// </summary>
    public static string? ExtractExecutablePath(string command)
    {
        var trimmed = Environment.ExpandEnvironmentVariables(command.Trim());
        if (trimmed.Length == 0)
            return null;

        if (trimmed[0] == '"')
        {
            var closing = trimmed.IndexOf('"', 1);
            return closing > 1 ? trimmed[1..closing] : null;
        }

        var spaceIndex = trimmed.IndexOf(' ', StringComparison.Ordinal);
        if (spaceIndex < 0)
            return trimmed;

        // Unquoted with spaces: accumulate tokens until one ends in a known
        // executable extension ("C:\Program Files\App\app.exe -tray").
        var searchFrom = 0;
        while (true)
        {
            var nextSpace = trimmed.IndexOf(' ', searchFrom);
            var candidate = nextSpace < 0 ? trimmed : trimmed[..nextSpace];
            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                candidate.EndsWith(".com", StringComparison.OrdinalIgnoreCase) ||
                candidate.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                candidate.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
            if (nextSpace < 0)
                return trimmed[..spaceIndex]; // no extension found — first token is the best guess
            searchFrom = nextSpace + 1;
        }
    }

    private StartupFileMetadata? GetMetadata(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
            return null;

        if (_metadataCache.TryGetValue(executablePath, out var cached))
            return cached;

        var metadata = _fileMetadataReader(executablePath);
        _metadataCache[executablePath] = metadata;
        return metadata;
    }

    private static StartupFileMetadata ReadFileMetadata(string executablePath)
    {
        try
        {
            if (!File.Exists(executablePath))
                return new StartupFileMetadata(null, null);

            var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            return new StartupFileMetadata(
                string.IsNullOrWhiteSpace(versionInfo.CompanyName) ? null : versionInfo.CompanyName,
                string.IsNullOrWhiteSpace(versionInfo.FileDescription) ? null : versionInfo.FileDescription);
        }
        catch
        {
            return new StartupFileMetadata(null, null);
        }
    }
}
