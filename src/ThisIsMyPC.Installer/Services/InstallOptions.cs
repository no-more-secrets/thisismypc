namespace ThisIsMyPC.Installer.Services;

/// <summary>What the user chose on the options page.</summary>
public sealed record InstallOptions(
    string InstallFolder,
    bool DesktopShortcut,
    bool StartWithWindows,
    bool CheckForUpdates);

/// <summary>Result of one install run. <see cref="LogPath"/> is the Windows Installer log.</summary>
public sealed record InstallOutcome(
    bool Succeeded,
    bool RebootRequired,
    string? Error,
    string? LogPath);

public interface IInstallEngine
{
    /// <summary>False when this build carries no MSI (a dev build); the UI then refuses to install.</summary>
    bool HasPackage { get; }

    /// <summary>Runs the MSI quietly with the chosen folder, then applies the other choices.</summary>
    Task<InstallOutcome> InstallAsync(InstallOptions options, IProgress<string> progress, CancellationToken cancellationToken);

    /// <summary>Starts the installed app (the Velopack stub in the install folder).</summary>
    void Launch(string installFolder);
}

/// <summary>Folder chooser; the view supplies the real one, tests a fake.</summary>
public interface IFolderPicker
{
    Task<string?> PickAsync(string startFolder);
}
