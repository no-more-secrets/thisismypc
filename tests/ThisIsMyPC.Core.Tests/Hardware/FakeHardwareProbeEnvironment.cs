using ThisIsMyPC.Core.Hardware.Detection;

namespace ThisIsMyPC.Core.Tests.Hardware;

/// <summary>A scripted machine: files, folders, processes and the OpenRGB socket, set by the test.</summary>
internal sealed class FakeHardwareProbeEnvironment : IHardwareProbeEnvironment
{
    public ProbeFolders Folders { get; init; } = new(
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        @"C:\Users\tester\AppData\Local",
        @"C:\Users\tester\AppData\Roaming");

    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Process image name (no extension) to its path; empty string means running with an unreadable path.</summary>
    public Dictionary<string, string> Processes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public OpenRgbProbeResult OpenRgb { get; set; } = OpenRgbProbeResult.Unreachable("no listener");
    public int OpenRgbProbes { get; private set; }

    public void AddFile(string path)
    {
        Files.Add(path);
        var dir = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(dir))
        {
            Directories.Add(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    public bool FileExists(string path) => Files.Contains(path);

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public IReadOnlyList<string> EnumerateDirectories(string parent, string pattern) =>
        Directories
            .Where(d => string.Equals(Path.GetDirectoryName(d), parent, StringComparison.OrdinalIgnoreCase))
            .Where(d => Matches(Path.GetFileName(d), pattern))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<string> EnumerateFiles(string directory, string pattern) =>
        Files
            .Where(f => string.Equals(Path.GetDirectoryName(f), directory, StringComparison.OrdinalIgnoreCase))
            .Where(f => Matches(Path.GetFileName(f), pattern))
            .ToList();

    public string? RunningProcessPath(string processName) =>
        Processes.TryGetValue(processName, out var path) ? path : null;

    public OpenRgbProbeResult ProbeOpenRgbServer(int port, TimeSpan timeout)
    {
        OpenRgbProbes++;
        return OpenRgb;
    }

    private static bool Matches(string name, string pattern)
    {
        if (pattern == "*")
            return true;
        if (pattern.EndsWith('*'))
            return name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        if (pattern.StartsWith('*'))
            return name.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
