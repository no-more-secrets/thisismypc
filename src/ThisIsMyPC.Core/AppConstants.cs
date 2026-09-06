namespace ThisIsMyPC.Core;

public static class AppConstants
{
    /// <summary>
    /// UI-owned settings, history, sets, monitoring state, and logs. The unelevated
    /// Avalonia process may parse this data. Privileged code must never trust it.
    /// </summary>
    public static string UserDataDirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThisIsMyPC");

    /// <summary>
    /// The machine-scoped trusted directory (%ProgramData%\ThisIsMyPC). The
    /// privilege broker and Session 0 service use it for restoration state. Created and
    /// DACL-hardened (Administrators/SYSTEM only) at startup; ProgramData's
    /// default ACL would let a standard user rewrite state that an elevated app
    /// and a SYSTEM service trust. Not a profile folder: users own their profile
    /// directories, and ownership beats a DACL.
    /// </summary>
    public static string DataDirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ThisIsMyPC");

    public const string RepositoryUrl = "https://github.com/No-More-Secrets/thisismypc";
    public const string UpdateUrl = RepositoryUrl + "/releases";
    public const string BugReportUrl = RepositoryUrl + "/issues/new?template=bug-report.md";

    /// <summary>Legal publisher name, as shown in About and on the signing certificate.</summary>
    public const string PublisherName = "No More Secrets, LLC";

    /// <summary>
    /// Short publisher name (No More Secrets, LLC): the MSI publisher line and
    /// the vendor folder under Program Files. Directory.Build.props (Company)
    /// and build-release.ps1 (-Authors) carry the same value for the build.
    /// </summary>
    public const string PublisherShortName = "NMS";
}
