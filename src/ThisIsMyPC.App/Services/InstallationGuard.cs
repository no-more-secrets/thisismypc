using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.Services;

public sealed class InstallationGuard : IInstallationGuard
{
    public bool IsProtectedLocation { get; }
    public string? WarningMessage { get; }

    public InstallationGuard(string appDirectory)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        IsProtectedLocation =
            IsUnderPath(appDirectory, programFiles) ||
            IsUnderPath(appDirectory, programFilesX86);

        if (!IsProtectedLocation)
        {
            WarningMessage =
                $"ThisIsMyPC is running from '{appDirectory}', which may be writable by non-administrator processes. " +
                @"For security, install to C:\Program Files\ThisIsMyPC\ to prevent DLL planting attacks. " +
                "Privileged operations may be restricted.";
        }
    }

    private static bool IsUnderPath(string directory, string parentPath)
        => !string.IsNullOrEmpty(parentPath) &&
           directory.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase);
}
