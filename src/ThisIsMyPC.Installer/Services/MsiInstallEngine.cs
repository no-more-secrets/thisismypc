using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.Installer.Services;

/// <summary>
/// Runs the embedded Velopack MSI through msiexec with no UI of its own (the
/// installer window is the UI), then applies the choices the MSI cannot
/// express: no Desktop shortcut, start with Windows, update checks.
/// </summary>
public sealed class MsiInstallEngine : IInstallEngine
{
    private readonly EmbeddedPackage _package;

    public MsiInstallEngine(EmbeddedPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        _package = package;
    }

    public bool HasPackage => _package.IsPresent;

    public async Task<InstallOutcome> InstallAsync(InstallOptions options, IProgress<string> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);

        // The data directory is the app's; hardening it first means the
        // unpacked MSI, the install log, and settings.json are
        // Administrators/SYSTEM-only from the first byte. The app re-verifies
        // the DACL at every start.
        string dataDir;
        string logPath;
        string scratch;
        try
        {
            dataDir = HardenedDataDirectory.Ensure();
            var logDir = Path.Combine(dataDir, "logs");
            Directory.CreateDirectory(logDir);
            logPath = Path.Combine(logDir, $"install-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.log");
            scratch = HardenedDataDirectory.NewScratch("msi");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new InstallOutcome(false, false, ex.Message, null);
        }

        try
        {
            progress.Report("Unpacking...");
            var msiPath = _package.ExtractTo(scratch);

            progress.Report("Installing ThisIsMyPC...");
            var exitCode = await RunMsiExecAsync(msiPath, options.InstallFolder, logPath, options.Reinstall, cancellationToken).ConfigureAwait(false);
            var result = MsiExitCodes.Describe(exitCode);
            if (!result.Succeeded)
                return new InstallOutcome(false, false, result.Message, logPath);

            progress.Report("Applying your choices...");
            if (!options.DesktopShortcut)
                RemoveDesktopShortcut();
            WriteSettings(dataDir, options);

            return new InstallOutcome(true, result.RebootRequired, null, logPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return new InstallOutcome(false, false, ex.Message, logPath);
        }
        finally
        {
            TryDelete(scratch);
        }
    }

    public async Task<InstallOutcome> UninstallAsync(InstalledApp installed, IProgress<string> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(progress);

        try
        {
            if (!File.Exists(installed.UninstallerPath))
                return new InstallOutcome(false, false, "The uninstaller (Update.exe) is no longer in the install folder. Remove ThisIsMyPC from Settings, Apps, Installed apps.", null);

            progress.Report("Removing ThisIsMyPC...");
            // Velopack's own uninstall: shortcuts, the install folder, the
            // Apps entry. --silent because this window already asked.
            var start = new ProcessStartInfo
            {
                FileName = installed.UninstallerPath,
                Arguments = "uninstall --silent",
                WorkingDirectory = Path.GetTempPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("The uninstaller did not start.");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
                return new InstallOutcome(false, false, $"The uninstaller stopped with error {process.ExitCode}.", null);

            // Velopack removes the folder; the stub can linger for a moment
            // while Explorer lets go of it, so the folder itself is the check.
            return new InstallOutcome(true, false, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return new InstallOutcome(false, false, ex.Message, null);
        }
    }

    public void Launch(string installFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installFolder);
        // ThisIsMyPC.exe is the Velopack stub that starts current\ThisIsMyPC.App.exe.
        var stub = Path.Combine(installFolder, "ThisIsMyPC.exe");
        if (!File.Exists(stub))
            return;
        using var process = Process.Start(new ProcessStartInfo(stub) { UseShellExecute = true, WorkingDirectory = installFolder });
    }

    /// <summary>
    /// The msiexec command line. Quiet, no restart, the folder through the
    /// property the Velopack MSI reads, and a verbose log for support. A
    /// reinstall of the version already present needs REINSTALL/REINSTALLMODE,
    /// or Windows Installer answers 1638 (already installed).
    /// </summary>
    public static string BuildMsiExecArguments(string msiPath, string installFolder, string logPath, bool reinstall = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(msiPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(installFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        // A trailing backslash before a closing quote escapes the quote for
        // the Installer's parser; strip it.
        var folder = installFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var reinstallArgs = reinstall ? " REINSTALL=ALL REINSTALLMODE=vomus" : string.Empty;
        return $"/i \"{msiPath}\" /qn /norestart VELOPACK_INSTALLDIR=\"{folder}\"{reinstallArgs} /l*v \"{logPath}\"";
    }

    private static async Task<int> RunMsiExecAsync(string msiPath, string installFolder, string logPath, bool reinstall, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
            Arguments = BuildMsiExecArguments(msiPath, installFolder, logPath, reinstall),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Windows Installer (msiexec.exe) did not start.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    /// <summary>The MSI is per-machine (ALLUSERS=1), so its Desktop shortcut lands on the Public desktop.</summary>
    private static void RemoveDesktopShortcut()
    {
        var link = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            InstallFolderRules.AppFolderName + ".lnk");
        if (File.Exists(link))
            File.Delete(link);
    }

    /// <summary>
    /// Writes through the app's own SettingsService so the file shape is the
    /// app's. AutoStartService.Reconcile() turns the auto-start setting into
    /// the Run entry at the app's next start; the installer never touches the
    /// registry itself.
    /// </summary>
    private static void WriteSettings(string dataDir, InstallOptions options)
    {
        var settings = new SettingsService(Path.Combine(dataDir, "settings.json"));
        settings.Initialize();
        var autoStart = options.StartWithWindows ? "1" : "0";
        settings.SetApp(AppSettingKeys.AutoStart, autoStart);
        settings.SetApp(AppSettingKeys.TrayMode, autoStart);
        settings.SetApp(AppSettingKeys.UpdateCheck, options.CheckForUpdates ? "1" : "0");
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Scratch inside the hardened data directory; a leftover copy of
            // the MSI is harmless and the next run gets a fresh folder.
        }
    }
}
