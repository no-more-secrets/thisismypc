namespace ThisIsMyPC.Installer.Services;

/// <summary>Meaning of a Windows Installer exit code, in words for the Done page.</summary>
public sealed record MsiResult(bool Succeeded, bool RebootRequired, string? Message);

public static class MsiExitCodes
{
    public const int Success = 0;
    public const int SuccessRebootRequired = 3010;
    public const int UserCancelled = 1602;
    public const int FatalError = 1603;
    public const int InstallInProgress = 1618;
    public const int PackageOpenFailed = 1619;
    public const int PackageInvalid = 1620;
    public const int AlreadyInstalled = 1638;

    public static MsiResult Describe(int exitCode) => exitCode switch
    {
        Success => new MsiResult(true, false, null),
        SuccessRebootRequired => new MsiResult(true, true, null),
        UserCancelled => new MsiResult(false, false, "The installation was cancelled."),
        FatalError => new MsiResult(false, false, "Windows Installer stopped with an error. Nothing was changed."),
        InstallInProgress => new MsiResult(false, false, "Another installation is running. Wait for it to finish, then try again."),
        PackageOpenFailed or PackageInvalid => new MsiResult(false, false, "The installer package could not be read. Download it again."),
        AlreadyInstalled => new MsiResult(false, false,
            "This version of ThisIsMyPC is already installed. Run this installer again and choose Uninstall on the first page, or remove it from Settings, Apps, Installed apps."),
        _ => new MsiResult(false, false, $"Windows Installer returned error {exitCode}."),
    };
}
