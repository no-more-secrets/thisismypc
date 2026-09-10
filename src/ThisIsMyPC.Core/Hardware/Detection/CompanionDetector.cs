using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Hardware.Detection;

/// <summary>What detection found for one companion, with the evidence trail.</summary>
public sealed record CompanionDetection(
    CompanionObservation Observation,
    string? LaunchPath,
    IReadOnlyList<string> Notes);

/// <summary>
/// Looks for every companion the policy knows, by the sources listed in
/// docs/planning/hardware-compatibility.md: uninstall entries, known folders,
/// winget portable folders, scheduled tasks, services, autorun values and
/// running processes. Each companion always gets an observation, so a
/// companion that is not found is recorded as NotInstalled (looked, absent),
/// never left out (not checked). Ownership is only set where the evidence
/// shows the program driving a domain; a running process alone never counts.
/// </summary>
public sealed class CompanionDetector
{
    private const string RunKeyPath = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string FanControlTaskPath = @"\FanControl";

    private static readonly string[] UninstallRoots =
    [
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    // Unverified beyond Sam's machines; each name is a candidate, and a
    // miss on all of them means "not found", which is fine for detection.
    private static readonly string[] ArmouryCrateServices =
        ["ArmouryCrateService", "ArmouryCrateControlInterface", "AsusAppService", "ASUSOptimization", "LightingService"];

    private readonly IRegistryService _registry;
    private readonly IHardwareProbeEnvironment _environment;
    private readonly IScheduledTaskService? _tasks;
    private readonly IServiceControlService? _services;

    public CompanionDetector(
        IRegistryService registry,
        IHardwareProbeEnvironment environment,
        IScheduledTaskService? tasks = null,
        IServiceControlService? services = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(environment);
        _registry = registry;
        _environment = environment;
        _tasks = tasks;
        _services = services;
    }

    /// <summary>
    /// One detection per companion. <paramref name="openRgb"/> is the SDK probe
    /// result when the provider ran one, so OpenRGB's ownership of Lighting can
    /// be recorded; <paramref name="asusPlatformDriverPresent"/> gates Armoury
    /// Crate's ownership of the ASUS platform domains.
    /// </summary>
    public IReadOnlyList<CompanionDetection> DetectAll(bool? asusPlatformDriverPresent, OpenRgbProbeResult? openRgb)
    {
        var uninstall = ReadUninstallEntries();
        return
        [
            DetectOpenRgb(uninstall, openRgb),
            DetectFanControl(uninstall),
            DetectGHelper(),
            DetectArmouryCrate(uninstall, asusPlatformDriverPresent),
            DetectSignalRgb(uninstall),
            DetectLibreHardwareMonitor(uninstall),
            DetectHwInfo(uninstall),
        ];
    }

    private sealed record UninstallEntry(string DisplayName, string? InstallLocation, string? DisplayIcon);

    private CompanionDetection DetectOpenRgb(IReadOnlyList<UninstallEntry> uninstall, OpenRgbProbeResult? probe)
    {
        var notes = new List<string>();
        var candidates = new List<string>();
        var running = RunningPath(CompanionApp.OpenRgb, "OpenRGB", notes, candidates);

        foreach (var entry in uninstall.Where(e => e.DisplayName.StartsWith("OpenRGB", StringComparison.OrdinalIgnoreCase)))
        {
            notes.Add($"OpenRGB: uninstall entry '{entry.DisplayName}'.");
            AddExeCandidates(entry, "OpenRGB.exe", candidates);
        }

        var folders = _environment.Folders;
        candidates.Add(Path.Combine(folders.ProgramFiles, "OpenRGB", "OpenRGB.exe"));
        candidates.Add(Path.Combine(folders.LocalAppData, "Programs", "OpenRGB", "OpenRGB.exe"));
        candidates.AddRange(WingetPortableExes("OpenRGB.OpenRGB*", "OpenRGB.exe"));

        var hasRun = _environment.DirectoryExists(Path.Combine(folders.RoamingAppData, "OpenRGB"));
        if (hasRun)
            notes.Add("OpenRGB: settings folder present (it has run before).");

        var launch = FirstExisting(candidates);
        var installed = launch is not null || hasRun
            || uninstall.Any(e => e.DisplayName.StartsWith("OpenRGB", StringComparison.OrdinalIgnoreCase));
        if (probe is not null)
            notes.Add($"OpenRGB SDK server: {probe.Detail ?? (probe.Reachable ? "answered" : "no answer")}.");

        var owns = running && probe is { Reachable: true, DeviceCount: > 0 }
            ? new[] { HardwareDomain.Lighting }
            : [];
        return Finish(CompanionApp.OpenRgb, installed, running, owns, launch, notes);
    }

    private CompanionDetection DetectFanControl(IReadOnlyList<UninstallEntry> uninstall)
    {
        var notes = new List<string>();
        var candidates = new List<string>();
        var running = RunningPath(CompanionApp.FanControl, "FanControl", notes, candidates);

        if (_tasks is not null)
        {
            var task = _tasks.Query(FanControlTaskPath);
            if (task is { IsSuccess: true, Value: { } info })
            {
                notes.Add($"FanControl: scheduled task '{info.Path}' ({(info.IsEnabled ? "enabled" : "disabled")}).");
                var command = StripQuotes(info.Command);
                if (!string.IsNullOrEmpty(command))
                {
                    if (Path.IsPathRooted(command))
                        candidates.Add(command);
                    else if (!string.IsNullOrEmpty(info.WorkingDirectory))
                        candidates.Add(Path.Combine(info.WorkingDirectory, command));
                }
            }
        }

        foreach (var entry in uninstall.Where(e => e.DisplayName.StartsWith("FanControl", StringComparison.OrdinalIgnoreCase)))
        {
            notes.Add($"FanControl: uninstall entry '{entry.DisplayName}'.");
            AddExeCandidates(entry, "FanControl.exe", candidates);
        }

        var folders = _environment.Folders;
        foreach (var parent in new[] { folders.ProgramFilesX86, folders.ProgramFiles, Path.Combine(folders.LocalAppData, "Programs") })
        {
            foreach (var dir in _environment.EnumerateDirectories(parent, "FanControl*"))
                candidates.Add(Path.Combine(dir, "FanControl.exe"));
        }
        candidates.AddRange(WingetPortableExes("Rem0o.FanControl*", "FanControl.exe"));

        var launch = FirstExisting(candidates);
        var installed = launch is not null;
        var owns = new List<HardwareDomain>();
        if (running && launch is not null)
        {
            var configDir = Path.Combine(Path.GetDirectoryName(launch) ?? string.Empty, "Configurations");
            if (_environment.EnumerateFiles(configDir, "*.json").Count > 0)
            {
                notes.Add("FanControl: a saved configuration is present next to the running program.");
                owns.Add(HardwareDomain.Cooling);
            }
        }
        return Finish(CompanionApp.FanControl, installed, running, owns, launch, notes);
    }

    private CompanionDetection DetectGHelper()
    {
        var notes = new List<string>();
        var candidates = new List<string>();
        var running = RunningPath(CompanionApp.GHelper, "GHelper", notes, candidates);

        var run = _registry.ReadString(RunKeyPath, "GHelper");
        if (run is { IsSuccess: true, Value: { Length: > 0 } value })
        {
            notes.Add("G-Helper: autorun value present in the user's Run key.");
            var exe = StripQuotes(value);
            if (!string.IsNullOrEmpty(exe))
                candidates.Add(exe);
        }

        var folders = _environment.Folders;
        var configPresent = _environment.FileExists(Path.Combine(folders.RoamingAppData, "GHelper", "config.json"));
        if (configPresent)
            notes.Add("G-Helper: configuration file present (it has run before).");

        candidates.AddRange(WingetPortableExes("seerge.g-helper*", "GHelper.exe"));
        candidates.Add(Path.Combine(folders.LocalAppData, "Microsoft", "WinGet", "Links", "GHelper.exe"));

        var launch = FirstExisting(candidates);
        var installed = launch is not null || configPresent;
        return Finish(CompanionApp.GHelper, installed, running, [], launch, notes);
    }

    private CompanionDetection DetectArmouryCrate(IReadOnlyList<UninstallEntry> uninstall, bool? asusPlatformDriverPresent)
    {
        var notes = new List<string>();
        var installed = false;
        var running = false;
        var lightingServiceRunning = false;
        var crateServiceRunning = false;

        foreach (var entry in uninstall.Where(e => e.DisplayName.Contains("Armoury Crate", StringComparison.OrdinalIgnoreCase)))
        {
            installed = true;
            notes.Add($"Armoury Crate: uninstall entry '{entry.DisplayName}'.");
        }

        if (_services is not null)
        {
            foreach (var name in ArmouryCrateServices)
            {
                var query = _services.Query(name);
                if (query is not { IsSuccess: true, Value: { } status })
                    continue;
                installed = true;
                var isRunning = status.State == ServiceState.Running;
                notes.Add($"Armoury Crate: service {name} is {(isRunning ? "running" : "not running")}.");
                running |= isRunning;
                lightingServiceRunning |= isRunning && name == "LightingService";
                crateServiceRunning |= isRunning && name == "ArmouryCrateService";
            }
        }

        var owns = new List<HardwareDomain>();
        if (lightingServiceRunning)
            owns.Add(HardwareDomain.Lighting);
        if (crateServiceRunning && asusPlatformDriverPresent is true)
        {
            owns.Add(HardwareDomain.SystemControl);
            owns.Add(HardwareDomain.Cooling);
        }
        return Finish(CompanionApp.ArmouryCrate, installed, running, owns, null, notes);
    }

    private CompanionDetection DetectSignalRgb(IReadOnlyList<UninstallEntry> uninstall)
    {
        var notes = new List<string>();
        var candidates = new List<string>();
        var running = RunningPath(CompanionApp.SignalRgb, "SignalRgb", notes, candidates)
            | RunningPath(CompanionApp.SignalRgb, "SignalRgbLauncher", notes, candidates);

        var installed = false;
        foreach (var entry in uninstall.Where(e => e.DisplayName.StartsWith("SignalRGB", StringComparison.OrdinalIgnoreCase)))
        {
            installed = true;
            notes.Add($"SignalRGB: uninstall entry '{entry.DisplayName}'.");
            AddExeCandidates(entry, "SignalRgbLauncher.exe", candidates);
        }
        return Finish(CompanionApp.SignalRgb, installed, running, [], FirstExisting(candidates), notes);
    }

    private CompanionDetection DetectLibreHardwareMonitor(IReadOnlyList<UninstallEntry> uninstall)
    {
        var notes = new List<string>();
        var candidates = new List<string>();
        var running = RunningPath(CompanionApp.LibreHardwareMonitor, "LibreHardwareMonitor", notes, candidates);

        var installed = false;
        foreach (var entry in uninstall.Where(e => e.DisplayName.StartsWith("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase)))
        {
            installed = true;
            notes.Add($"LibreHardwareMonitor: uninstall entry '{entry.DisplayName}'.");
            AddExeCandidates(entry, "LibreHardwareMonitor.exe", candidates);
        }
        var folders = _environment.Folders;
        candidates.Add(Path.Combine(folders.ProgramFiles, "LibreHardwareMonitor", "LibreHardwareMonitor.exe"));
        candidates.AddRange(WingetPortableExes("LibreHardwareMonitor.LibreHardwareMonitor*", "LibreHardwareMonitor.exe"));

        var launch = FirstExisting(candidates);
        return Finish(CompanionApp.LibreHardwareMonitor, installed || launch is not null, running, [], launch, notes);
    }

    private CompanionDetection DetectHwInfo(IReadOnlyList<UninstallEntry> uninstall)
    {
        var notes = new List<string>();
        var candidates = new List<string>();
        var running = RunningPath(CompanionApp.HwInfo, "HWiNFO64", notes, candidates)
            | RunningPath(CompanionApp.HwInfo, "HWiNFO32", notes, candidates);

        var installed = false;
        foreach (var entry in uninstall.Where(e => e.DisplayName.StartsWith("HWiNFO", StringComparison.OrdinalIgnoreCase)))
        {
            installed = true;
            notes.Add($"HWiNFO: uninstall entry '{entry.DisplayName}'.");
            AddExeCandidates(entry, "HWiNFO64.exe", candidates);
        }
        foreach (var key in new[] { @"HKCU\Software\HWiNFO64", @"HKCU\Software\HWiNFO32" })
        {
            if (_registry.KeyExists(key) is { IsSuccess: true, Value: true })
            {
                installed = true;
                notes.Add($"HWiNFO: settings key {key} present.");
            }
        }
        return Finish(CompanionApp.HwInfo, installed, running, [], FirstExisting(candidates), notes);
    }

    // ---- helpers ----

    /// <summary><paramref name="installed"/> is the install evidence alone; a running program counts as installed here, once.</summary>
    private static CompanionDetection Finish(
        CompanionApp app, bool installed, bool running, IReadOnlyList<HardwareDomain> owns, string? launch, List<string> notes)
    {
        if (!installed && !running)
            notes.Add($"{CompanionNames.Of(app)}: not found.");
        var observation = new CompanionObservation(app, installed || running, running, owns);
        return new CompanionDetection(observation, launch, notes);
    }

    /// <summary>Records a running process for the notes and its path as the first launch candidate. Returns whether it runs.</summary>
    private bool RunningPath(CompanionApp app, string processName, List<string> notes, List<string> candidates)
    {
        var path = _environment.RunningProcessPath(processName);
        if (path is null)
            return false;
        notes.Add(path.Length > 0
            ? $"{CompanionNames.Of(app)}: process {processName} is running from {path}."
            : $"{CompanionNames.Of(app)}: process {processName} is running (path not readable).");
        if (path.Length > 0)
            candidates.Insert(0, path);
        return true;
    }

    /// <summary>
    /// DisplayIcon counts only when it names the program itself; installers
    /// often point it at their uninstaller, which must never become the
    /// launch path.
    /// </summary>
    private static void AddExeCandidates(UninstallEntry entry, string exeName, List<string> candidates)
    {
        var icon = StripQuotes(entry.DisplayIcon);
        if (!string.IsNullOrEmpty(icon))
        {
            var comma = icon.LastIndexOf(',');
            if (comma > 0 && int.TryParse(icon[(comma + 1)..], out _))
                icon = icon[..comma];
            if (string.Equals(Path.GetFileName(icon), exeName, StringComparison.OrdinalIgnoreCase))
                candidates.Add(icon);
        }
        var location = StripQuotes(entry.InstallLocation);
        if (!string.IsNullOrEmpty(location))
            candidates.Add(Path.Combine(location, exeName));
    }

    private IEnumerable<string> WingetPortableExes(string packagePattern, string exeName)
    {
        var packages = Path.Combine(_environment.Folders.LocalAppData, "Microsoft", "WinGet", "Packages");
        foreach (var dir in _environment.EnumerateDirectories(packages, packagePattern))
        {
            yield return Path.Combine(dir, exeName);
            foreach (var nested in _environment.EnumerateDirectories(dir, "*"))
                yield return Path.Combine(nested, exeName);
        }
    }

    private string? FirstExisting(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && _environment.FileExists(candidate))
                return candidate;
        }
        return null;
    }

    private List<UninstallEntry> ReadUninstallEntries()
    {
        var entries = new List<UninstallEntry>();
        foreach (var root in UninstallRoots)
        {
            var subKeys = _registry.EnumerateSubKeys(root);
            if (!subKeys.IsSuccess || subKeys.Value is null)
                continue;
            foreach (var name in subKeys.Value)
            {
                var keyPath = root + "\\" + name;
                var displayName = _registry.ReadString(keyPath, "DisplayName");
                if (displayName is not { IsSuccess: true, Value: { Length: > 0 } })
                    continue;
                entries.Add(new UninstallEntry(
                    displayName.Value,
                    ReadOptional(keyPath, "InstallLocation"),
                    ReadOptional(keyPath, "DisplayIcon")));
            }
        }
        return entries;
    }

    private string? ReadOptional(string keyPath, string valueName)
    {
        var read = _registry.ReadString(keyPath, valueName);
        return read is { IsSuccess: true, Value: { Length: > 0 } value } ? value : null;
    }

    /// <summary>The executable part of a command line: the quoted token, or everything up to and including ".exe".</summary>
    private static string? StripQuotes(string? value)
    {
        if (value is null)
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"')
        {
            var close = trimmed.IndexOf('"', 1);
            return close > 0 ? trimmed[1..close] : trimmed.Trim('"');
        }
        var exe = trimmed.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? trimmed[..(exe + 4)] : trimmed;
    }
}
