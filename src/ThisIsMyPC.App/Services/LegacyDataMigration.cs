using NLog;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// One-shot move of pre-machine-scope data: builds before the packaging switch
/// stored state in %APPDATA%\ThisIsMyPC; the machine-scoped app reads only
/// %ProgramData%\ThisIsMyPC. Copies what the new location does not already have
/// (never overwrites, never deletes the old directory), then drops a marker so
/// the scan runs once per profile. Only the launching user's profile is
/// reachable; other profiles' old data stays where it is, which matches the
/// one-database rule going forward.
/// </summary>
internal static class LegacyDataMigration
{
    private const string MarkerFileName = "migrated-to-programdata.txt";

    public static void CopyFromUserProfile(string machineDataDir, ILogger logger)
    {
        try
        {
            var legacyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ThisIsMyPC");

            if (!Directory.Exists(legacyDir)
                || string.Equals(legacyDir, machineDataDir, StringComparison.OrdinalIgnoreCase)
                || File.Exists(Path.Combine(legacyDir, MarkerFileName)))
            {
                return;
            }

            var copied = 0;
            foreach (var file in Directory.GetFiles(legacyDir))
            {
                var target = Path.Combine(machineDataDir, Path.GetFileName(file));
                if (!File.Exists(target))
                {
                    File.Copy(file, target);
                    copied++;
                }
            }

            foreach (var dir in Directory.GetDirectories(legacyDir))
            {
                var name = Path.GetFileName(dir);
                if (string.Equals(name, "logs", StringComparison.OrdinalIgnoreCase))
                    continue;
                copied += CopyDirectoryIfMissing(dir, Path.Combine(machineDataDir, name));
            }

            File.WriteAllText(
                Path.Combine(legacyDir, MarkerFileName),
                $"Data moved to {machineDataDir} on {DateTime.Now:yyyy-MM-dd}. This folder is no longer read.");
            logger.Info(
                "Migrated {Count} legacy items from {Legacy} to {Machine}", copied, legacyDir, machineDataDir);
        }
#pragma warning disable CA1031 // Migration is best-effort; a failure must not block startup
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.Warn(ex, "Legacy data migration failed; continuing with a fresh machine data directory");
        }
    }

    private static int CopyDirectoryIfMissing(string source, string target)
    {
        var copied = 0;
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            var destination = Path.Combine(target, Path.GetFileName(file));
            if (!File.Exists(destination))
            {
                File.Copy(file, destination);
                copied++;
            }
        }

        foreach (var dir in Directory.GetDirectories(source))
            copied += CopyDirectoryIfMissing(dir, Path.Combine(target, Path.GetFileName(dir)));
        return copied;
    }
}
