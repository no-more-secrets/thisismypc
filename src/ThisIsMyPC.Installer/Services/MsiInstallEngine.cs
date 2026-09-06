using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;
using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Settings;
using ThisIsMyPC.Interop.Win32.Security;

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

        var folderCheck = InstallFolderRules.Check(options.InstallFolder);
        if (!folderCheck.IsValid)
            return new InstallOutcome(false, false, folderCheck.Error, null);

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
            var logDir = HardenedDataDirectory.EnsureChildDirectory("logs");
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
#if !DEBUG
            var trust = AuthenticodeVerifier.VerifyTrusted(
                msiPath,
                "No More Secrets, LLC",
                exactSignerName: true);
            if (!trust.IsSuccess)
                return new InstallOutcome(
                    false,
                    false,
                    "The embedded Windows Installer signature is invalid. " + trust.ErrorMessage,
                    logPath);
#endif

            progress.Report("Installing ThisIsMyPC...");
            var exitCode = await RunMsiExecAsync(msiPath, options.InstallFolder, logPath, options.Reinstall, cancellationToken).ConfigureAwait(false);
            var result = MsiExitCodes.Describe(exitCode);
            if (!result.Succeeded)
                return new InstallOutcome(false, false, result.Message, logPath);

            progress.Report("Applying your choices...");
            if (!options.DesktopShortcut)
                RemoveShortcut(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!options.StartMenuShortcut)
                RemoveShortcut(Environment.SpecialFolder.CommonPrograms);
            WritePendingSettings(options);

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

        if (!IsExpectedUninstaller(installed))
        {
            return new InstallOutcome(
                false,
                false,
                "The install location is not protected. Remove ThisIsMyPC from Settings, Apps, Installed apps.",
                null);
        }

        try
        {
            if (!File.Exists(installed.UninstallerPath))
                return new InstallOutcome(false, false, "The uninstaller (Update.exe) is no longer in the install folder. Remove ThisIsMyPC from Settings, Apps, Installed apps.", null);

            var trust = AuthenticodeVerifier.VerifyTrusted(
                installed.UninstallerPath,
                "No More Secrets, LLC",
                exactSignerName: true);
            if (!trust.IsSuccess)
            {
                return new InstallOutcome(
                    false,
                    false,
                    "The installed uninstaller signature is invalid. Remove ThisIsMyPC from Settings, Apps, Installed apps. " + trust.ErrorMessage,
                    null);
            }

            progress.Report("Removing ThisIsMyPC...");
            // Velopack's own uninstall: shortcuts, the install folder, the
            // Apps entry. --silent because this window already asked.
            var start = new ProcessStartInfo
            {
                FileName = installed.UninstallerPath,
                Arguments = "uninstall --silent",
                WorkingDirectory = installed.InstallFolder,
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
        if (!InstallFolderRules.IsUnderProgramFiles(installFolder))
            return;
        // ThisIsMyPC.exe is the Velopack stub that starts current\ThisIsMyPC.App.exe.
        var stub = Path.Combine(installFolder, "ThisIsMyPC.exe");
        if (!File.Exists(stub))
            return;
        var trust = AuthenticodeVerifier.VerifyTrusted(stub, "No More Secrets, LLC", exactSignerName: true);
        if (!trust.IsSuccess)
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

    internal static bool IsExpectedUninstaller(InstalledApp installed)
    {
        try
        {
            return InstallFolderRules.IsUnderProgramFiles(installed.InstallFolder) &&
                Path.GetFullPath(installed.UninstallerPath).Equals(
                    Path.Combine(Path.GetFullPath(installed.InstallFolder), "Update.exe"),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static async Task<int> RunMsiExecAsync(string msiPath, string installFolder, string logPath, bool reinstall, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
            Arguments = BuildMsiExecArguments(msiPath, installFolder, logPath, reinstall),
            WorkingDirectory = Environment.SystemDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Windows Installer (msiexec.exe) did not start.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    /// <summary>
    /// The MSI is per-machine (ALLUSERS=1), so Velopack puts its shortcuts on
    /// the Public desktop and in the all-users Start menu. vpk creates both at
    /// pack time; an unticked box means deleting the file afterwards.
    /// </summary>
    private static void RemoveShortcut(Environment.SpecialFolder folder)
    {
        var link = Path.Combine(Environment.GetFolderPath(folder), InstallFolderRules.AppFolderName + ".lnk");
        if (File.Exists(link))
            File.Delete(link);
    }

    /// <summary>
    /// Stores untrusted UI preferences in HKCU. The unelevated app imports and
    /// removes them. The elevated installer never writes into a user-controlled
    /// profile directory, which could contain a reparse point.
    /// </summary>
    private static void WritePendingSettings(InstallOptions options)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\No More Secrets\ThisIsMyPC\InstallOptions", writable: true);
        key.SetValue(AppSettingKeys.AutoStart, options.StartWithWindows ? "1" : "0", RegistryValueKind.String);
        key.SetValue(AppSettingKeys.TrayMode, options.StartWithWindows ? "1" : "0", RegistryValueKind.String);
        key.SetValue(AppSettingKeys.UpdateCheck, options.CheckForUpdates ? "1" : "0", RegistryValueKind.String);
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
