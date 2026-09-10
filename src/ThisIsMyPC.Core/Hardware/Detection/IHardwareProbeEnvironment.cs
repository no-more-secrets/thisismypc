namespace ThisIsMyPC.Core.Hardware.Detection;

/// <summary>Result of one OpenRGB SDK server probe.</summary>
/// <param name="Reachable">A TCP connection to the server port succeeded and the server answered a controller-count request.</param>
/// <param name="DeviceCount">Controllers the server reported; null when the server did not answer.</param>
/// <param name="Detail">Why the probe stopped, for the evidence list and logs.</param>
public sealed record OpenRgbProbeResult(bool Reachable, int? DeviceCount, string? Detail = null)
{
    public static OpenRgbProbeResult Unreachable(string detail) => new(false, null, detail);
}

/// <summary>Well-known folders the detector probes. Resolved by the environment so tests can redirect them.</summary>
public sealed record ProbeFolders(
    string ProgramFiles,
    string ProgramFilesX86,
    string LocalAppData,
    string RoamingAppData);

/// <summary>
/// The machine reads the Hardware tabs need beyond the shared inventory, the
/// registry, the task scheduler and the service manager: the file system,
/// running-process paths, and the OpenRGB SDK socket. The Win32 layer
/// implements it; tests supply a scripted one. Every member is a read.
/// </summary>
public interface IHardwareProbeEnvironment
{
    ProbeFolders Folders { get; }

    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <summary>Immediate subdirectories of <paramref name="parent"/> matching <paramref name="pattern"/>; empty when the parent is missing.</summary>
    IReadOnlyList<string> EnumerateDirectories(string parent, string pattern);

    /// <summary>Files directly in <paramref name="directory"/> matching <paramref name="pattern"/>; empty when the directory is missing.</summary>
    IReadOnlyList<string> EnumerateFiles(string directory, string pattern);

    /// <summary>
    /// Full path of the first running process with this image name (without
    /// extension), an empty string when it runs but its path is unreadable,
    /// or null when no such process runs.
    /// </summary>
    string? RunningProcessPath(string processName);

    OpenRgbProbeResult ProbeOpenRgbServer(int port, TimeSpan timeout);
}
