using ThisIsMyPC.Installer.Services;

namespace ThisIsMyPC.Installer.Tests;

public class PreviewInstallEngineTests
{
    [Fact]
    public async Task PreviewCompletesWithoutAccessingTheInstallLocation()
    {
        var engine = new PreviewInstallEngine();
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var options = new InstallOptions(folder, true, true, true);
        var progress = new Progress<string>();
        Assert.True((await engine.InstallAsync(options, progress, CancellationToken.None)).Succeeded);
        Assert.True((await engine.UninstallAsync(new InstalledApp("1.0.0", folder, "missing.exe"), progress, CancellationToken.None)).Succeeded);
        engine.Launch(folder);
        Assert.False(Directory.Exists(folder));
    }
}
