using ThisIsMyPC.Core;

namespace ThisIsMyPC.Installer.Services;

/// <summary>Outcome of checking a typed or picked install folder.</summary>
public sealed record FolderCheck(bool IsValid, string? Error, string? Warning);

/// <summary>
/// Pure rules for the install folder: the default the MSI would pick on its
/// own, and the checks the options page runs on every keystroke.
/// </summary>
public static class InstallFolderRules
{
    public const string AppFolderName = "ThisIsMyPC";

    /// <summary>What Velopack's PerMachine default resolves to: Program Files\{publisher}\{title}.</summary>
    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        AppConstants.PublisherShortName,
        AppFolderName);

    /// <summary>
    /// A picked parent folder becomes parent\ThisIsMyPC unless the user
    /// already pointed at a folder by that name.
    /// </summary>
    public static string WithAppFolder(string pickedFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pickedFolder);
        var trimmed = pickedFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Path.GetFileName(trimmed), AppFolderName, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : Path.Combine(trimmed, AppFolderName);
    }

    public static FolderCheck Check(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return new FolderCheck(false, "Choose a folder.", null);

        if (folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return new FolderCheck(false, "The folder name contains characters Windows does not allow.", null);

        if (!Path.IsPathFullyQualified(folder))
            return new FolderCheck(false, "Use a full path that starts with a drive letter, like C:\\.", null);

        var root = Path.GetPathRoot(folder);
        if (string.Equals(Path.TrimEndingDirectorySeparator(folder), Path.TrimEndingDirectorySeparator(root ?? ""), StringComparison.OrdinalIgnoreCase))
            return new FolderCheck(false, "Pick a folder, not the whole drive.", null);

        return new FolderCheck(true, null, IsUnderProgramFiles(folder) ? null :
            "This folder is outside Program Files, so other programs on this PC can change the files in it. ThisIsMyPC will warn about that every time it starts.");
    }

    /// <summary>Mirrors the app's InstallationGuard: Program Files or Program Files (x86).</summary>
    public static bool IsUnderProgramFiles(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return IsUnder(folder, programFiles) || IsUnder(folder, programFilesX86);
    }

    private static bool IsUnder(string folder, string parent)
    {
        if (string.IsNullOrEmpty(parent))
            return false;
        var normalizedParent = Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar;
        return folder.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }
}
