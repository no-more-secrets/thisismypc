using ThisIsMyPC.Installer.Services;
using ThisIsMyPC.Installer.ViewModels;

namespace ThisIsMyPC.Installer.Tests;

public class InstallerViewModelTests
{
    private sealed class FakeEngine : IInstallEngine
    {
        public bool HasPackage { get; set; } = true;
        public InstallOptions? Received { get; private set; }
        public string? Launched { get; private set; }
        public InstallOutcome Outcome { get; set; } = new(true, false, null, @"C:\log.txt");

        public Task<InstallOutcome> InstallAsync(InstallOptions options, IProgress<string> progress, CancellationToken cancellationToken)
        {
            Received = options;
            progress.Report("Installing ThisIsMyPC...");
            return Task.FromResult(Outcome);
        }

        public void Launch(string installFolder) => Launched = installFolder;
    }

    private sealed class FakePicker(string? result) : IFolderPicker
    {
        public Task<string?> PickAsync(string startFolder) => Task.FromResult(result);
    }

    [Fact]
    public async Task HappyPath_WelcomeLicenseOptionsInstallDoneLaunch()
    {
        var engine = new FakeEngine();
        var closed = false;
        var vm = new InstallerViewModel(engine, "LICENSE TEXT", existing: null) { RequestClose = () => closed = true };

        Assert.Equal(InstallStep.Welcome, vm.Step);
        Assert.Equal("Next", vm.PrimaryButtonText);
        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.Equal(InstallStep.License, vm.Step);
        Assert.False(vm.CanGoPrimary);
        vm.LicenseAccepted = true;
        Assert.True(vm.CanGoPrimary);
        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.Equal(InstallStep.Options, vm.Step);
        Assert.Equal("Install", vm.PrimaryButtonText);
        vm.DesktopShortcut = false;
        vm.StartWithWindows = true;
        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.Equal(InstallStep.Done, vm.Step);
        Assert.False(vm.Failed);
        Assert.Equal("Finish", vm.PrimaryButtonText);
        Assert.NotNull(engine.Received);
        Assert.Equal(InstallFolderRules.DefaultFolder, engine.Received.InstallFolder);
        Assert.False(engine.Received.DesktopShortcut);
        Assert.True(engine.Received.StartWithWindows);
        Assert.True(engine.Received.CheckForUpdates);

        await vm.PrimaryCommand.ExecuteAsync(null);
        Assert.Equal(InstallFolderRules.DefaultFolder, engine.Launched);
        Assert.True(closed);
    }

    [Fact]
    public async Task Failure_ShowsMessageAndLogAndCloseButtonWithoutLaunching()
    {
        var engine = new FakeEngine { Outcome = new InstallOutcome(false, false, "It broke.", @"C:\log.txt") };
        var closed = false;
        var vm = new InstallerViewModel(engine, "L", existing: null) { RequestClose = () => closed = true, LicenseAccepted = true };
        vm.Step = InstallStep.Options;

        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.Equal(InstallStep.Done, vm.Step);
        Assert.True(vm.Failed);
        Assert.Equal("It broke.", vm.ErrorText);
        Assert.Equal(@"C:\log.txt", vm.LogPath);
        Assert.Equal("Close", vm.PrimaryButtonText);
        Assert.Equal("Not installed", vm.StepCaption);

        await vm.PrimaryCommand.ExecuteAsync(null);
        Assert.Null(engine.Launched);
        Assert.True(closed);
    }

    [Fact]
    public async Task NoPackage_RefusesToInstall()
    {
        var engine = new FakeEngine { HasPackage = false };
        var vm = new InstallerViewModel(engine, "L", existing: null) { LicenseAccepted = true };
        vm.Step = InstallStep.Options;

        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.True(vm.Failed);
        Assert.Null(engine.Received);
        Assert.Contains("no package", vm.ErrorText);
    }

    [Fact]
    public void BadFolder_BlocksInstallAndExplains()
    {
        var vm = new InstallerViewModel(new FakeEngine(), "L", existing: null) { LicenseAccepted = true };
        vm.Step = InstallStep.Options;
        Assert.True(vm.CanGoPrimary);

        vm.InstallFolder = @"C:\";
        Assert.False(vm.CanGoPrimary);
        Assert.True(vm.HasFolderError);

        vm.InstallFolder = @"D:\Apps\ThisIsMyPC";
        Assert.True(vm.CanGoPrimary);
        Assert.False(vm.HasFolderError);
        Assert.True(vm.HasFolderWarning);
    }

    [Fact]
    public async Task Browse_AppendsAppFolderToThePickedParent()
    {
        var vm = new InstallerViewModel(new FakeEngine(), "L", existing: null) { FolderPicker = new FakePicker(@"D:\Apps") };
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(@"D:\Apps\ThisIsMyPC", vm.InstallFolder);

        vm.FolderPicker = new FakePicker(null);
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(@"D:\Apps\ThisIsMyPC", vm.InstallFolder);
    }

    [Fact]
    public void Upgrade_StartsFromExistingChoicesAndSaysUpdate()
    {
        var existing = new InstallOptions(@"C:\X\ThisIsMyPC", true, StartWithWindows: true, CheckForUpdates: false);
        var vm = new InstallerViewModel(new FakeEngine(), "L", existing) { LicenseAccepted = true };
        Assert.True(vm.IsUpgrade);
        Assert.True(vm.StartWithWindows);
        Assert.False(vm.CheckForUpdates);
        vm.Step = InstallStep.Options;
        Assert.Equal("Update", vm.PrimaryButtonText);
    }

    [Fact]
    public void Back_WalksLicenseAndOptionsOnly()
    {
        var vm = new InstallerViewModel(new FakeEngine(), "L", existing: null);
        Assert.False(vm.CanGoBack);
        vm.Step = InstallStep.Options;
        vm.BackCommand.Execute(null);
        Assert.Equal(InstallStep.License, vm.Step);
        vm.BackCommand.Execute(null);
        Assert.Equal(InstallStep.Welcome, vm.Step);
        Assert.False(vm.CanGoBack);
    }
}
