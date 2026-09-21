namespace ThisIsMyPC.Installer.Services;

/// <summary>Interactive preview with no installation, removal, settings writes, or app launch.</summary>
internal sealed class PreviewInstallEngine : IInstallEngine
{
    public bool HasPackage => true;

    public async Task<InstallOutcome> InstallAsync(InstallOptions options, IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report("Preview only. No changes will be made.");
        await Task.Delay(700, cancellationToken).ConfigureAwait(false);
        return new(true, false, null, null);
    }

    public Task<InstallOutcome> UninstallAsync(InstalledApp installed, IProgress<string> progress, CancellationToken cancellationToken)
        => Task.FromResult(new InstallOutcome(true, false, null, null));

    public void Launch(string installFolder) { }
}
