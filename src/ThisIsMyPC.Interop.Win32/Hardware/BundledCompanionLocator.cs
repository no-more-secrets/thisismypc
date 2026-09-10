using ThisIsMyPC.Core.Hardware;

namespace ThisIsMyPC.Interop.Win32.Hardware;

/// <summary>
/// Where the companions bundled with the app live: <c>companions\OpenRGB</c>
/// next to the executable (the release build copies the pinned OpenRGB there).
/// Inside a source checkout, where nothing is installed, the pinned archive
/// extracted by tools/get-openrgb-archive.ps1 stands in, so development and
/// diagnostic runs drive the same bundled build.
/// </summary>
public static class BundledCompanionLocator
{
    public const string CompanionsFolderName = "companions";

    public static string? Find(CompanionApp app) => app switch
    {
        CompanionApp.OpenRgb => FindOpenRgb(),
        _ => null,
    };

    private static string? FindOpenRgb()
    {
        var installed = Path.Combine(AppContext.BaseDirectory, CompanionsFolderName, "OpenRGB", "OpenRGB.exe");
        if (File.Exists(installed))
            return installed;

        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        if (repoRoot is null)
            return null;

        var cache = Path.Combine(repoRoot, "artifacts", "tool-cache", "openrgb");
        if (!Directory.Exists(cache))
            return null;
        // The most recently extracted pin wins; version strings do not sort.
        return Directory.GetDirectories(cache)
            .Select(d => Path.Combine(d, "extracted", "OpenRGB Windows 64-bit", "OpenRGB.exe"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ThisIsMyPC.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }
}
