namespace ThisIsMyPC.Core;

public static class AppConstants
{
    public static string DataDirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ThisIsMyPC");

    public const string UpdateUrl = "https://github.com/No-More-Secrets/thisismypc/releases";
}
