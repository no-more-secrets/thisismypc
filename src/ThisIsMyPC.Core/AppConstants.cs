namespace ThisIsMyPC.Core;

public static class AppConstants
{
    public static string DataDirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ThisIsMyPC");

    /// <summary>
    /// Machine-wide data shared with the Session 0 service (28-3): the SYSTEM
    /// watchdog cannot resolve a user's roaming profile, so the drift baseline
    /// lives under ProgramData.
    /// </summary>
    public static string MachineDataDirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ThisIsMyPC");

    public const string UpdateUrl = "https://github.com/No-More-Secrets/thisismypc/releases";
}
