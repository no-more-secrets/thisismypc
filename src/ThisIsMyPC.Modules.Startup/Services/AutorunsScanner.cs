using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Services;

/// <summary>
/// Builds the Autoruns inventory: every location in <see cref="AutorunLocations"/>,
/// both Startup folders, every scheduled task, and the auto-start services and
/// drivers under the Services key. Disabled items are read from the places
/// Autoruns parks them (AutorunsDisabled subkeys, subfolders, and values), so
/// the two tools agree on state.
/// </summary>
public sealed class AutorunsScanner
{
    private const int ServiceTypeKernelDriver = 0x1;
    private const int ServiceTypeFileSystemDriver = 0x2;
    private const int ServiceTypeRecognizerDriver = 0x8;
    private const int ServiceTypeWin32 = 0x30;
    private const int ServiceTypeUserInstance = 0x80;

    private readonly IRegistryService _registry;
    private readonly IStartupFolderService _folders;
    private readonly Func<string, StartupFileMetadata> _fileMetadataReader;
    private readonly Dictionary<string, StartupFileMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _system32;
    private readonly string _sysWow64;

    public AutorunsScanner(
        IRegistryService registry,
        IStartupFolderService folders,
        Func<string, StartupFileMetadata>? fileMetadataReader = null,
        string? windowsDirectory = null)
    {
        _registry = registry;
        _folders = folders;
        _fileMetadataReader = fileMetadataReader ?? StartupScanner.ReadFileMetadata;
        var windows = windowsDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        _system32 = Path.Combine(windows, "System32");
        _sysWow64 = Path.Combine(windows, "SysWOW64");
    }

    /// <summary>
    /// Scans every category. Tasks and Win32 service display data come from the
    /// module's other scanners so the COM and SCM enumerations run once.
    /// </summary>
    public IReadOnlyList<AutorunEntry> Scan(
        IReadOnlyList<ScheduledTaskEntry> scheduledTasks,
        IReadOnlyList<ServiceEntry> services)
    {
        ArgumentNullException.ThrowIfNull(scheduledTasks);
        ArgumentNullException.ThrowIfNull(services);

        var entries = new List<AutorunEntry>();
        foreach (var location in AutorunLocations.Registry)
        {
            if (location.Kind == AutorunItemKind.RegistryValue)
                ScanValues(entries, location);
            else
                ScanSubKeys(entries, location);
        }

        ScanStartupFolder(entries, StartupFolderScope.CurrentUser, StartupScanner.UserApprovedStartupFolderKey);
        ScanStartupFolder(entries, StartupFolderScope.AllUsers, StartupScanner.MachineApprovedStartupFolderKey);

        foreach (var task in scheduledTasks)
        {
            entries.Add(new AutorunEntry
            {
                Category = AutorunCategory.ScheduledTasks,
                Kind = AutorunItemKind.ScheduledTask,
                Name = task.Name,
                Location = task.Path,
                Data = task.Path,
                Description = task.Description,
                Publisher = task.Author,
                IsEnabled = task.IsEnabled,
            });
        }

        ScanServices(entries, services);
        return AddFileFacts(CollapseReRegistered(entries));
    }

    /// <summary>Whether the image file is there, when it was last written, and when its location key or folder was.</summary>
    private List<AutorunEntry> AddFileFacts(List<AutorunEntry> entries)
    {
        var locationTimes = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var exists = true;
            DateTime? timestamp = null;
            if (entry.ImagePath is { } image)
            {
                try
                {
                    exists = File.Exists(image);
                    if (exists)
                        timestamp = File.GetLastWriteTime(image);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    exists = false;
                }
            }

            if (!locationTimes.TryGetValue(entry.Location, out var locationTime))
            {
                locationTime = LocationTime(entry.Kind, entry.Location);
                locationTimes[entry.Location] = locationTime;
            }

            entries[i] = entry with { FileExists = exists, Timestamp = timestamp, LocationTimestamp = locationTime };
        }
        return entries;
    }

    private DateTime? LocationTime(AutorunItemKind kind, string location)
    {
        try
        {
            return kind switch
            {
                AutorunItemKind.StartupFile => Directory.Exists(location) ? Directory.GetLastWriteTime(location) : null,
                AutorunItemKind.ScheduledTask => null,
                _ => _registry.GetLastWriteTime(location),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// A live item beside its parked twin means the program re-registered
    /// itself after the user switched it off (Autoruns lists both and refuses
    /// to touch either). The pair becomes one live row that carries a
    /// snapshot of the live copy, so switching it off purges the copy and
    /// undo puts it back; the parked twin stays as the user's choice.
    /// </summary>
    private List<AutorunEntry> CollapseReRegistered(List<AutorunEntry> entries)
    {
        var parked = entries
            .Where(e => !e.IsEnabled && e.Kind is AutorunItemKind.RegistryValue or AutorunItemKind.RegistryKey or AutorunItemKind.StartupFile)
            .Select(e => $"{e.Kind}|{e.Location}|{e.Name}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (parked.Count == 0)
            return entries;

        var result = new List<AutorunEntry>(entries.Count);
        foreach (var entry in entries)
        {
            var key = $"{entry.Kind}|{entry.Location}|{entry.Name}";
            if (!entry.IsEnabled || !parked.Contains(key))
            {
                result.Add(entry);
                continue;
            }

            var snapshot = AutorunSnapshot.Capture(_registry, _folders, entry.Kind, entry.Location, entry.Name);
            result.Add(entry with
            {
                Note = "Re-registered itself after being switched off",
                LiveSnapshot = snapshot?.Serialize(),
                // Without a snapshot the copy cannot be purged undoably; the switch stays off-limits.
                CanToggle = snapshot is not null,
            });
        }
        // The parked twins drop out: their row is the flagged live one.
        return result.Where(e => e.IsEnabled || !parked.Contains($"{e.Kind}|{e.Location}|{e.Name}") || !HasLiveTwin(entries, e)).ToList();
    }

    private static bool HasLiveTwin(List<AutorunEntry> entries, AutorunEntry parkedEntry)
        => entries.Any(e => e.IsEnabled && e.Kind == parkedEntry.Kind
            && string.Equals(e.Location, parkedEntry.Location, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Name, parkedEntry.Name, StringComparison.OrdinalIgnoreCase));

    // ---- Registry values ----

    private void ScanValues(List<AutorunEntry> entries, AutorunLocation location)
    {
        AddValues(entries, location, location.KeyPath, enabled: true);
        AddValues(entries, location, $@"{location.KeyPath}\{AutorunTarget.DisabledName}", enabled: false);
    }

    private void AddValues(List<AutorunEntry> entries, AutorunLocation location, string keyPath, bool enabled)
    {
        var names = _registry.EnumerateValues(keyPath);
        if (!names.IsSuccess || names.Value is null)
            return;

        foreach (var name in names.Value)
        {
            if (name.Length == 0)
                continue;
            if (location.OnlyValues is not null && !location.OnlyValues.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            if (location.SkipValues is not null && location.SkipValues.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            var data = ReadText(keyPath, name);
            if (string.IsNullOrWhiteSpace(data))
                continue;

            var image = ResolveValueImage(location, data);
            var metadata = GetMetadata(image);
            entries.Add(new AutorunEntry
            {
                Category = location.Category,
                Kind = AutorunItemKind.RegistryValue,
                Name = name,
                Location = location.KeyPath,
                Data = data,
                ImagePath = image,
                Description = metadata?.Description,
                Publisher = metadata?.Publisher,
                IsEnabled = enabled,
                Note = enabled ? TaskManagerNote(location.KeyPath, name) : null,
            });
        }
    }

    /// <summary>Run keys: a command line. Font Drivers, Drivers32, Known DLLs: a DLL, bare names living in System32 (or SysWOW64 for the WOW key).</summary>
    private string? ResolveValueImage(AutorunLocation location, string data)
    {
        if (location.Category == AutorunCategory.Logon)
            return StartupScanner.ExtractExecutablePath(data);
        return ResolveDll(data, location.Is32Bit);
    }

    // ---- Registry subkeys ----

    private void ScanSubKeys(List<AutorunEntry> entries, AutorunLocation location)
    {
        AddSubKeys(entries, location, location.KeyPath, enabled: true);
        AddSubKeys(entries, location, $@"{location.KeyPath}\{AutorunTarget.DisabledName}", enabled: false);
    }

    private void AddSubKeys(List<AutorunEntry> entries, AutorunLocation location, string keyPath, bool enabled)
    {
        var names = _registry.EnumerateSubKeys(keyPath);
        if (!names.IsSuccess || names.Value is null)
            return;

        foreach (var name in names.Value)
        {
            if (name.Equals(AutorunTarget.DisabledName, StringComparison.OrdinalIgnoreCase))
                continue;

            var itemKey = $@"{keyPath}\{name}";
            var data = ReadText(itemKey, location.DataValueName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(data))
            {
                if (location.RequireData)
                    continue;
                // A CLSID-named subkey with no default value still names its handler.
                data = name;
            }

            var description = location.DescriptionValueName is null
                ? (location.DataValueName is null ? null : ReadText(itemKey, string.Empty))
                : ReadText(itemKey, location.DescriptionValueName);

            // Shell handlers the Context Menus page switched off: a dash before
            // the CLSID in (Default), or the CLSID on the Blocked list. That page
            // owns their state; here they show as off with the switch greyed.
            var shellOff = location.Category == AutorunCategory.Explorer && enabled && IsShellDisabled(name, ref data);

            var image = ResolveSubKeyImage(location, name, data);
            var metadata = GetMetadata(image);
            entries.Add(new AutorunEntry
            {
                Category = location.Category,
                Kind = AutorunItemKind.RegistryKey,
                Name = name,
                Location = location.KeyPath,
                Data = data,
                ImagePath = image,
                Description = FirstNonEmpty(description, ClsidDescription(name, data, location.Is32Bit), metadata?.Description),
                Publisher = metadata?.Publisher,
                IsEnabled = enabled && !shellOff,
                Note = shellOff ? "Off in Context Menus" : null,
                CanToggle = !shellOff,
            });
        }
    }

    /// <summary>ShellExView's dash convention ("-{CLSID}") and Windows' own Blocked list, both used by the Context Menus page.</summary>
    private bool IsShellDisabled(string keyName, ref string data)
    {
        if (data.Length > 1 && data[0] == '-' && IsClsid(data[1..]))
        {
            data = data[1..];
            return true;
        }
        var clsid = IsClsid(data) ? data : keyName;
        return IsClsid(clsid)
            && _registry.ValueExists(AutorunLocations.BlockedShellExtensionsKey, clsid) is { IsSuccess: true, Value: true };
    }

    private string? ResolveSubKeyImage(AutorunLocation location, string keyName, string data)
    {
        switch (location.Category)
        {
            case AutorunCategory.Logon:
                return StartupScanner.ExtractExecutablePath(data);
            case AutorunCategory.WinsockProviders:
                return ExpandPath(data);
            case AutorunCategory.PrintMonitors:
                return ResolveDll(data, is32Bit: false);
            case AutorunCategory.Office:
                return ResolveProgIdImage(keyName, location.Is32Bit);
            default:
                // Shell handlers, BHOs, credential providers: the data or the key name is a CLSID.
                return ResolveClsidImage(IsClsid(data) ? data : keyName, location.Is32Bit);
        }
    }

    private static bool IsClsid(string text) => text.Length == 38 && text[0] == '{' && text[^1] == '}';

    /// <summary>InprocServer32 of a CLSID, from the 64-bit or the WOW6432Node class table.</summary>
    private string? ResolveClsidImage(string clsid, bool is32Bit)
    {
        if (!IsClsid(clsid))
            return null;
        foreach (var root in ClassRoots(is32Bit))
        {
            var server = ReadText($@"{root}\CLSID\{clsid}\InprocServer32", string.Empty);
            if (!string.IsNullOrWhiteSpace(server))
                return ExpandPath(server);
            server = ReadText($@"{root}\CLSID\{clsid}\LocalServer32", string.Empty);
            if (!string.IsNullOrWhiteSpace(server))
                return StartupScanner.ExtractExecutablePath(server);
        }
        return null;
    }

    private string? ClsidDescription(string keyName, string data, bool is32Bit)
    {
        var clsid = IsClsid(data) ? data : keyName;
        if (!IsClsid(clsid))
            return null;
        foreach (var root in ClassRoots(is32Bit))
        {
            var text = ReadText($@"{root}\CLSID\{clsid}", string.Empty);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }
        return null;
    }

    private string? ResolveProgIdImage(string progId, bool is32Bit)
    {
        foreach (var root in ClassRoots(is32Bit))
        {
            var clsid = ReadText($@"{root}\{progId}\CLSID", string.Empty);
            if (!string.IsNullOrWhiteSpace(clsid))
                return ResolveClsidImage(clsid, is32Bit);
        }
        return null;
    }

    private static IEnumerable<string> ClassRoots(bool is32Bit) => is32Bit
        ? [@"HKLM\SOFTWARE\WOW6432Node\Classes", @"HKLM\SOFTWARE\Classes", @"HKCU\SOFTWARE\Classes"]
        : [@"HKLM\SOFTWARE\Classes", @"HKCU\SOFTWARE\Classes", @"HKLM\SOFTWARE\WOW6432Node\Classes"];

    // ---- Startup folders ----

    private void ScanStartupFolder(List<AutorunEntry> entries, StartupFolderScope scope, string approvedKey)
    {
        AddFolderItems(entries, _folders.Enumerate(scope).Value, enabled: true, approvedKey);
        AddFolderItems(entries, _folders.EnumerateDisabled(scope).Value, enabled: false, approvedKey);
    }

    private void AddFolderItems(List<AutorunEntry> entries, IReadOnlyList<StartupFolderItem>? items, bool enabled, string approvedKey)
    {
        if (items is null)
            return;
        foreach (var item in items)
        {
            var fileName = Path.GetFileName(item.FilePath);
            var folder = Path.GetDirectoryName(item.FilePath) ?? item.FilePath;
            if (!enabled && folder.EndsWith(AutorunTarget.DisabledName, StringComparison.OrdinalIgnoreCase))
                folder = Path.GetDirectoryName(folder) ?? folder;

            var image = item.ResolvedTarget
                ?? (Path.GetExtension(item.FilePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? item.FilePath : null);
            var metadata = GetMetadata(image);
            entries.Add(new AutorunEntry
            {
                Category = AutorunCategory.Logon,
                Kind = AutorunItemKind.StartupFile,
                Name = fileName,
                Location = folder,
                Data = item.FilePath,
                ImagePath = image,
                Description = metadata?.Description,
                Publisher = metadata?.Publisher,
                IsEnabled = enabled,
                Note = enabled ? TaskManagerNote(approvedKey, fileName, isApprovedKey: true) : null,
            });
        }
    }

    // ---- Services and drivers ----

    private void ScanServices(List<AutorunEntry> entries, IReadOnlyList<ServiceEntry> services)
    {
        var names = _registry.EnumerateSubKeys(AutorunLocations.ServicesKey);
        if (!names.IsSuccess || names.Value is null)
            return;

        var known = services.ToDictionary(s => s.ServiceName, StringComparer.OrdinalIgnoreCase);
        foreach (var name in names.Value)
        {
            var key = $@"{AutorunLocations.ServicesKey}\{name}";
            var type = _registry.ReadDWord(key, "Type");
            var start = _registry.ReadDWord(key, AutorunToggler.ServiceStartValue);
            if (!type.IsSuccess || !start.IsSuccess)
                continue;
            if ((type.Value & ServiceTypeUserInstance) != 0)
                continue; // per-user template instance; the template is the item

            var isDriver = (type.Value & (ServiceTypeKernelDriver | ServiceTypeFileSystemDriver | ServiceTypeRecognizerDriver)) != 0;
            var isService = (type.Value & ServiceTypeWin32) != 0;
            if (!isDriver && !isService)
                continue;

            var saved = _registry.ReadDWord(key, AutorunTarget.DisabledName);
            var disabledByAutoruns = start.Value == AutorunToggler.ServiceStartDisabled && saved.IsSuccess;
            var autoStart = start.Value is 0 or 1 or 2;
            if (!autoStart && !disabledByAutoruns)
                continue;

            var imagePath = ReadText(key, "ImagePath");
            var image = ResolveServiceImage(key, imagePath);
            var metadata = GetMetadata(image);
            known.TryGetValue(name, out var scm);
            var displayName = scm?.DisplayName ?? ReadText(key, "DisplayName");
            if (string.IsNullOrWhiteSpace(displayName) || displayName.StartsWith('@'))
                displayName = name;
            var description = scm?.Description ?? ReadText(key, "Description");
            if (description is not null && description.StartsWith('@'))
                description = null;

            entries.Add(new AutorunEntry
            {
                Category = isDriver ? AutorunCategory.Drivers : AutorunCategory.Services,
                Kind = AutorunItemKind.Service,
                Name = name,
                Location = AutorunLocations.ServicesKey,
                Data = string.IsNullOrWhiteSpace(imagePath) ? name : imagePath,
                ImagePath = image,
                Description = FirstNonEmpty(displayName == name ? null : displayName, description, metadata?.Description),
                Publisher = metadata?.Publisher,
                IsEnabled = !disabledByAutoruns,
                Note = StartNote(disabledByAutoruns ? saved.Value : start.Value),
            });
        }
    }

    private static string? StartNote(int start) => start switch
    {
        0 => "Boot start",
        1 => "System start",
        2 => "Automatic",
        3 => "Manual",
        _ => null,
    };

    /// <summary>ImagePath forms: quoted, \SystemRoot\..., \??\C:\..., system32\drivers\x.sys, and svchost hosts whose real code is Parameters\ServiceDll.</summary>
    private string? ResolveServiceImage(string key, string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return null;

        var exe = StartupScanner.ExtractExecutablePath(imagePath) ?? imagePath;
        if (exe.StartsWith(@"\??\", StringComparison.Ordinal))
            exe = exe[4..];
        if (exe.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            exe = Path.Combine(Path.GetDirectoryName(_system32) ?? _system32, exe[12..]);
        else if (exe.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase))
            exe = Path.Combine(Path.GetDirectoryName(_system32) ?? _system32, exe);
        else if (!Path.IsPathRooted(exe))
            exe = Path.Combine(Path.GetDirectoryName(_system32) ?? _system32, exe);

        if (Path.GetFileName(exe).Equals("svchost.exe", StringComparison.OrdinalIgnoreCase))
        {
            var dll = ReadText($@"{key}\Parameters", "ServiceDll");
            if (!string.IsNullOrWhiteSpace(dll))
                return ExpandPath(dll);
        }
        return exe;
    }

    // ---- Helpers ----

    /// <summary>String-typed values only; a DWORD or binary value under a text-only location is not an item.</summary>
    private string? ReadText(string keyPath, string valueName)
    {
        var read = _registry.ReadValue(keyPath, valueName);
        if (!read.IsSuccess || read.Value is null)
            return null;
        return read.Value.Kind is RegistryValueDataKind.String or RegistryValueDataKind.ExpandString
            ? read.Value.Data
            : null;
    }

    private string ResolveDll(string data, bool is32Bit)
    {
        var expanded = ExpandPath(data);
        if (Path.IsPathRooted(expanded))
            return expanded;
        return Path.Combine(is32Bit ? _sysWow64 : _system32, expanded);
    }

    private static string ExpandPath(string path)
        => Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

    private static string? FirstNonEmpty(params string?[] candidates)
        => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    /// <summary>"Off in Task Manager" when Windows' StartupApproved state (what Task Manager toggles) says disabled.</summary>
    private string? TaskManagerNote(string keyPath, string name, bool isApprovedKey = false)
    {
        var approvedKey = isApprovedKey ? keyPath : keyPath switch
        {
            StartupScanner.UserRunKey => StartupScanner.UserApprovedRunKey,
            StartupScanner.MachineRunKey => StartupScanner.MachineApprovedRunKey,
            StartupScanner.MachineRunWow64Key => StartupScanner.MachineApprovedRun32Key,
            _ => null,
        };
        if (approvedKey is null)
            return null;
        var blob = _registry.ReadBinary(approvedKey, name);
        if (!blob.IsSuccess || blob.Value is null || blob.Value.Length == 0)
            return null;
        return (blob.Value[0] & 1) == 1 ? "Off in Task Manager" : null;
    }

    private StartupFileMetadata? GetMetadata(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        if (_metadataCache.TryGetValue(path, out var cached))
            return cached;
        var metadata = _fileMetadataReader(path);
        _metadataCache[path] = metadata;
        return metadata;
    }
}
