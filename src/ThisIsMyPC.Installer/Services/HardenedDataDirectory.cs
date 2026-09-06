using ThisIsMyPC.Core;
using ThisIsMyPC.Interop.Win32;

namespace ThisIsMyPC.Installer.Services;

/// <summary>
/// Every file the installer writes and then trusts (the unpacked MSI, the
/// native libraries it loads) goes under the app's ProgramData folder with
/// the app's own DACL on it: Administrators and SYSTEM only. %TEMP% would be
/// the obvious place and is the wrong one: the same user's non-elevated
/// processes can write there, and swapping a file between our write and our
/// use would run their code with our elevation.
/// </summary>
public static class HardenedDataDirectory
{
    /// <summary>Creates and hardens the data directory; throws when the DACL cannot be trusted.</summary>
    public static string Ensure()
    {
        var dataDir = AppConstants.DataDirectoryPath;
        Directory.CreateDirectory(dataDir);
        if ((File.GetAttributes(dataDir) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException($"ThisIsMyPC refused a reparse point at its data folder: {dataDir}");
        var result = new DataDirectoryGuard().EnsureHardened(dataDir);
        if (!result.IsSuccess)
            throw new UnauthorizedAccessException(
                $"ThisIsMyPC cannot protect its data folder ({dataDir}): {result.ErrorMessage}");
        return dataDir;
    }

    /// <summary>A fresh, uniquely named scratch folder inside the hardened directory.</summary>
    public static string NewScratch(string purpose)
    {
        return EnsureChildDirectory("installer", purpose + "-" + Guid.NewGuid().ToString("N"));
    }

    internal static string EnsureChildDirectory(params string[] segments)
    {
        var current = Ensure();
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment) ||
                segment is "." or ".." ||
                segment.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            {
                throw new ArgumentException("A private directory segment is invalid.", nameof(segments));
            }

            current = Path.Combine(current, segment);
            Directory.CreateDirectory(current);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException($"ThisIsMyPC refused a reparse point in its private data path: {current}");
        }

        return current;
    }
}
