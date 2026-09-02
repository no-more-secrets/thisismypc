using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ThisIsMyPC.Installer.Services;

/// <summary>A copy of ThisIsMyPC already on this PC.</summary>
public sealed record InstalledApp(string Version, string InstallFolder, string UninstallerPath);

/// <summary>
/// Finds an existing install two ways: the Apps entry Velopack's Update.exe
/// registers at install, then the default folder itself (Update.exe plus the
/// version file Velopack keeps in current\). Either way the uninstaller is
/// Update.exe in the install folder.
/// </summary>
public static partial class InstalledAppDetector
{
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string DisplayName = "ThisIsMyPC";
    private const string UpdaterFileName = "Update.exe";

    public static InstalledApp? Detect() => FromRegistry() ?? FromFolder(InstallFolderRules.DefaultFolder);

    /// <summary>The install folder is the proof: Update.exe plus a version we can read.</summary>
    public static InstalledApp? FromFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;
        var updater = Path.Combine(folder, UpdaterFileName);
        if (!File.Exists(updater))
            return null;

        var version = ReadVersionFile(Path.Combine(folder, "current", "sq.version"))
            ?? ReadExeVersion(Path.Combine(folder, "current", "ThisIsMyPC.App.exe"));
        return version is null ? null : new InstalledApp(version, folder, updater);
    }

    private static InstalledApp? FromRegistry()
    {
        foreach (var (hive, view) in new[]
                 {
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry32),
                     (RegistryHive.CurrentUser, RegistryView.Registry64),
                 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(UninstallKeyPath);
                if (uninstall is null)
                    continue;

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(name);
                    if (key is null)
                        continue;
                    var displayName = key.GetValue("DisplayName") as string;
                    if (!string.Equals(displayName, DisplayName, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(name, DisplayName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var folder = key.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(folder))
                        folder = FolderFromUninstallString(key.GetValue("UninstallString") as string);
                    var found = FromFolder(folder?.TrimEnd(Path.DirectorySeparatorChar));
                    if (found is null)
                        continue;

                    var displayVersion = key.GetValue("DisplayVersion") as string;
                    return string.IsNullOrWhiteSpace(displayVersion) ? found : found with { Version = displayVersion };
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A hive we cannot read is not an install we can act on.
            }
        }

        return null;
    }

    /// <summary>Velopack's UninstallString is the quoted Update.exe path plus arguments.</summary>
    public static string? FolderFromUninstallString(string? uninstallString)
    {
        if (string.IsNullOrWhiteSpace(uninstallString))
            return null;
        var match = UpdaterPathPattern().Match(uninstallString);
        return match.Success ? Path.GetDirectoryName(match.Groups["path"].Value) : null;
    }

    /// <summary>The version element of Velopack's sq.version (a nuspec).</summary>
    public static string? ParseVersionFile(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;
        var match = VersionElementPattern().Match(xml);
        return match.Success ? match.Groups["version"].Value.Trim() : null;
    }

    private static string? ReadVersionFile(string path)
    {
        try
        {
            return File.Exists(path) ? ParseVersionFile(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadExeVersion(string path)
    {
        if (!File.Exists(path))
            return null;
        var info = FileVersionInfo.GetVersionInfo(path);
        return string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
    }

    [GeneratedRegex("\"?(?<path>[^\"]*?\\\\Update\\.exe)\"?", RegexOptions.IgnoreCase)]
    private static partial Regex UpdaterPathPattern();

    [GeneratedRegex("<version>(?<version>[^<]+)</version>", RegexOptions.IgnoreCase)]
    private static partial Regex VersionElementPattern();
}
