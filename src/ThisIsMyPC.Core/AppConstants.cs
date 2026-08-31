namespace ThisIsMyPC.Core;

public static class AppConstants
{
    /// <summary>
    /// The machine-scoped data directory (%ProgramData%\ThisIsMyPC): settings,
    /// change history, sets, monitoring state, and the drift baseline the
    /// Session 0 service consumes. The app corresponds to the PC, not a profile:
    /// one install, one database, all-profiles coverage. Created and
    /// DACL-hardened (Administrators/SYSTEM only) at startup; ProgramData's
    /// default ACL would let a standard user rewrite state that an elevated app
    /// and a SYSTEM service trust. Not a profile folder: users own their profile
    /// directories, and ownership beats a DACL.
    /// </summary>
    public static string DataDirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ThisIsMyPC");

    public const string UpdateUrl = "https://github.com/No-More-Secrets/thisismypc/releases";
}
