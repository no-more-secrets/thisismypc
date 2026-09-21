using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ThisIsMyPC.Installer.Services;

/// <summary>A copy of ThisIsMyPC already on this PC.</summary>
public sealed record InstalledApp(string Version, string InstallFolder, string UninstallerPath);

/// <summary>
/// Finds an existing install through its machine registration or the default
/// folder. Update.exe identifies the package, but MSI removal uses the
/// registered product code rather than the updater's uninstall command.
/// </summary>
public static partial class InstalledAppDetector
{
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string DisplayName = "ThisIsMyPC";
    private const string UpdaterFileName = "Update.exe";
    private const int MaxVersionFileBytes = 64 * 1024;

    public static InstalledApp? Detect() => FromRegistry() ?? FromFolder(InstallFolderRules.DefaultFolder);

    /// <summary>Resolve only the machine MSI registration associated with this protected folder.</summary>
    internal static string? FindMsiProductCode(string installFolder)
    {
        foreach (var (hive, view) in RegistryLocations())
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(UninstallKeyPath);
            using var alias = uninstall?.OpenSubKey("MSI:" + DisplayName);
            if (alias is null || !IsMatchingMsiFolder(installFolder,
                    alias.GetValue("InstallLocation") as string, alias.GetValue("DisplayName") as string))
                continue;

            var code = ParseMsiProductCode(alias.GetValue("UninstallString") as string);
            if (code is null)
                continue;
            using var product = uninstall!.OpenSubKey(code);
            if (product?.GetValue("WindowsInstaller") is int installer && installer == 1 &&
                string.Equals(product.GetValue("DisplayName") as string, DisplayName, StringComparison.OrdinalIgnoreCase))
                return code;
        }
        return null;
    }

    internal static bool IsMatchingMsiFolder(string expectedFolder, string? registeredFolder, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(registeredFolder) ||
            !string.Equals(displayName, DisplayName, StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedFolder)).Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(registeredFolder)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    internal static string? ParseMsiProductCode(string? uninstallString)
    {
        if (string.IsNullOrWhiteSpace(uninstallString))
            return null;
        var match = MsiUninstallPattern().Match(uninstallString);
        return match.Success && Guid.TryParseExact(match.Groups["code"].Value, "B", out var code) && code != Guid.Empty
            ? code.ToString("B").ToUpperInvariant() : null;
    }

    /// <summary>The install folder is the proof: Update.exe plus a version we can read.</summary>
    public static InstalledApp? FromFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;
        var updater = Path.Combine(folder, UpdaterFileName);
        if (!File.Exists(updater))
            return null;

        var version = ReadVersionFile(Path.Combine(folder, "current", "sq.version"));
        return version is null ? null : new InstalledApp(version, folder, updater);
    }

    private static InstalledApp? FromRegistry()
    {
        foreach (var (hive, view) in RegistryLocations())
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
                    // The current package owns sq.version. Old MSI product entries can remain
                    // registered and expose a stale four-part DisplayVersion for this folder.
                    return found;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A hive we cannot read is not an install we can act on.
            }
        }

        return null;
    }

    internal static IEnumerable<(RegistryHive Hive, RegistryView View)> RegistryLocations()
    {
        yield return (RegistryHive.LocalMachine, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, RegistryView.Registry32);
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
            var info = new FileInfo(path);
            return info.Exists && info.Length <= MaxVersionFileBytes
                ? ParseVersionFile(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex("\"?(?<path>[^\"]*?\\\\Update\\.exe)\"?", RegexOptions.IgnoreCase)]
    private static partial Regex UpdaterPathPattern();

    [GeneratedRegex("<version>(?<version>[^<]+)</version>", RegexOptions.IgnoreCase)]
    private static partial Regex VersionElementPattern();

    [GeneratedRegex("^\\s*(?:msiexec(?:\\.exe)?|\"(?:[^\"]*\\\\)?msiexec\\.exe\")\\s+/x\\s*(?<code>\\{[0-9a-f-]{36}\\})\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex MsiUninstallPattern();
}
