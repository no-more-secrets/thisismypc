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
        var result = new DataDirectoryGuard().EnsureHardened(dataDir);
        if (!result.IsSuccess)
            throw new UnauthorizedAccessException(
                $"ThisIsMyPC cannot protect its data folder ({dataDir}): {result.ErrorMessage}");
        return dataDir;
    }

    /// <summary>A fresh, uniquely named scratch folder inside the hardened directory.</summary>
    public static string NewScratch(string purpose)
    {
        var dir = Path.Combine(Ensure(), "installer", purpose + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
