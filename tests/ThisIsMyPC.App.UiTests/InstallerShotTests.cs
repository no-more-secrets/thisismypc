using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ThisIsMyPC.Installer.Services;
using ThisIsMyPC.Installer.ViewModels;
using ThisIsMyPC.Installer.Win32;

namespace ThisIsMyPC.App.UiTests;

/// <summary>Renders the Win32 installer through its production GDI renderer.</summary>
public class InstallerShotTests
{
    private const int Width = 720;
    private const int Height = 640;

    private sealed class FakeEngine : IInstallEngine
    {
        public bool HasPackage => true;
        public InstallOptions? Received { get; private set; }
        public InstalledApp? Uninstalled { get; private set; }
        public InstallOutcome Outcome { get; set; } = new(true, false, null, @"C:\ProgramData\ThisIsMyPC\logs\install.log");

        public Task<InstallOutcome> InstallAsync(InstallOptions options, IProgress<string> progress, CancellationToken cancellationToken)
        {
            Received = options;
            return Task.FromResult(Outcome);
        }

        public Task<InstallOutcome> UninstallAsync(InstalledApp installed, IProgress<string> progress, CancellationToken cancellationToken)
        {
            Uninstalled = installed;
            return Task.FromResult(new InstallOutcome(true, false, null, null));
        }

        public void Launch(string installFolder) { }
    }

    private static readonly string License = EmbeddedPackage.LoadLicenseText();

    [AvaloniaFact]
    public async Task Walkthrough_EveryPageRendersAndChoicesReachTheEngine()
    {
        var engine = new FakeEngine();
        var viewModel = new InstallerViewModel(engine, License, installed: null, existing: null);
        using var window = new InstallerWindow(viewModel);

        Save(viewModel, "welcome");
        Save(viewModel, "welcome-150-percent", 1080, 960);
        Assert.True(viewModel.CanGoPrimary);
        Assert.False(viewModel.CanGoBack);

        window.HandleLogicalClick(512, 602);
        Save(viewModel, "license");
        Assert.False(viewModel.CanGoPrimary);

        window.HandleLogicalClick(200, 528);
        window.HandleLogicalClick(512, 602);
        Save(viewModel, "options");

        window.HandleLogicalClick(200, 301);
        window.HandleLogicalClick(200, 369);
        window.HandleLogicalClick(512, 602);
        await WaitForAsync(() => viewModel.Step == InstallStep.Done);
        Assert.Equal(InstallStep.Done, viewModel.Step);
        Save(viewModel, "done");

        Assert.NotNull(engine.Received);
        Assert.False(engine.Received.DesktopShortcut);
        Assert.True(engine.Received.StartMenuShortcut);
        Assert.True(engine.Received.StartWithWindows);
        Assert.True(engine.Received.CheckForUpdates);
        Assert.Equal(InstallFolderRules.DefaultFolder, engine.Received.InstallFolder);
    }

    [AvaloniaFact]
    public void Options_BadFolderShowsTheReasonAndDisablesInstall()
    {
        var viewModel = new InstallerViewModel(new FakeEngine(), License, installed: null, existing: null)
        {
            LicenseAccepted = true,
            Step = InstallStep.Options,
            InstallFolder = @"D:\Apps\ThisIsMyPC",
        };
        Save(viewModel, "options-outside-program-files");
        Assert.NotNull(viewModel.FolderError);
        Assert.False(viewModel.CanGoPrimary);

        viewModel.InstallFolder = @"C:\";
        Save(viewModel, "options-drive-root");
        Assert.NotNull(viewModel.FolderError);
        Assert.False(viewModel.CanGoPrimary);
    }

    [AvaloniaFact]
    public async Task Installed_WelcomeNamesTheVersionAndUninstallWalksConfirmToRemoved()
    {
        var engine = new FakeEngine();
        var installed = new InstalledApp("0.0.9", @"C:\Program Files\NMS\ThisIsMyPC", @"C:\Program Files\NMS\ThisIsMyPC\Update.exe");
        var viewModel = new InstallerViewModel(engine, License, installed, existing: null);
        using var window = new InstallerWindow(viewModel);

        Save(viewModel, "welcome-installed");
        window.HandleLogicalClick(560, 285);
        Save(viewModel, "welcome-uninstall-ticked");
        window.HandleLogicalClick(512, 602);
        Save(viewModel, "confirm-uninstall");
        window.HandleLogicalClick(512, 602);
        await WaitForAsync(() => viewModel.Step == InstallStep.Done);
        Save(viewModel, "removed");

        Assert.Same(installed, engine.Uninstalled);
        Assert.True(viewModel.Removed);
        Assert.Equal(InstallStep.Done, viewModel.Step);
    }

    [AvaloniaFact]
    public void Installed_OptionsLocksTheFolderAndSaysUpdate()
    {
        var installed = new InstalledApp("0.0.9", @"C:\Program Files\NMS\ThisIsMyPC", "x");
        var viewModel = new InstallerViewModel(new FakeEngine(), License, installed, existing: null)
        {
            LicenseAccepted = true,
            Step = InstallStep.Options,
        };
        Save(viewModel, "options-update");
        Assert.False(viewModel.CanChooseFolder);
        Assert.Equal("Update", viewModel.PrimaryButtonText);
    }

    [AvaloniaFact]
    public void NewerInstalled_WelcomeBlocksNext()
    {
        var installed = new InstalledApp("99.0.0", @"C:\Program Files\NMS\ThisIsMyPC", "x");
        var viewModel = new InstallerViewModel(new FakeEngine(), License, installed, existing: null);
        Save(viewModel, "welcome-newer-installed");
        Assert.False(viewModel.CanGoPrimary);
    }

    [AvaloniaFact]
    public async Task Done_FailureShowsMessageAndLogPath()
    {
        var engine = new FakeEngine
        {
            Outcome = new InstallOutcome(false, false,
                "This version of ThisIsMyPC is already installed. Run this installer again and choose Uninstall on the first page.",
                @"C:\ProgramData\ThisIsMyPC\logs\install-20260901-210000.log"),
        };
        var viewModel = new InstallerViewModel(engine, License, installed: null, existing: null)
        {
            LicenseAccepted = true,
            Step = InstallStep.Options,
        };
        await viewModel.PrimaryCommand.ExecuteAsync(null);
        Save(viewModel, "done-failed");
        Assert.True(viewModel.Failed);
        Assert.Equal("Close", viewModel.PrimaryButtonText);
    }

    private static void Save(InstallerViewModel viewModel, string name, int width = Width, int height = Height)
    {
        var pixels = InstallerWindow.RenderPreviewBgra(viewModel, width, height);
        Assert.Equal(width * height * 4, pixels.Length);
        using var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        using (var framebuffer = bitmap.Lock())
        {
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(
                    pixels,
                    y * width * 4,
                    framebuffer.Address + y * framebuffer.RowBytes,
                    width * 4);
            }
        }

        var directory = Path.Combine(FindRepoRoot(), "artifacts", "ui-shots", "installer-win32");
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, name + ".png"));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ThisIsMyPC.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
