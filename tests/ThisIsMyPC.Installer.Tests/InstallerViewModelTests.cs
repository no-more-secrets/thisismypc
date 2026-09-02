using ThisIsMyPC.Installer.Services;
using ThisIsMyPC.Installer.ViewModels;

namespace ThisIsMyPC.Installer.Tests;

public class InstallerViewModelTests
{
    private sealed class FakeEngine : IInstallEngine
    {
        public bool HasPackage { get; set; } = true;
        public InstallOptions? Received { get; private set; }
        public InstalledApp? Uninstalled { get; private set; }
        public string? Launched { get; private set; }
        public InstallOutcome Outcome { get; set; } = new(true, false, null, @"C:\log.txt");
        public InstallOutcome UninstallOutcome { get; set; } = new(true, false, null, null);

        public Task<InstallOutcome> InstallAsync(InstallOptions options, IProgress<string> progress, CancellationToken cancellationToken)
        {
            Received = options;
            progress.Report("Installing ThisIsMyPC...");
            return Task.FromResult(Outcome);
        }

        public Task<InstallOutcome> UninstallAsync(InstalledApp installed, IProgress<string> progress, CancellationToken cancellationToken)
        {
            Uninstalled = installed;
            return Task.FromResult(UninstallOutcome);
        }

        public void Launch(string installFolder) => Launched = installFolder;
    }

    private sealed class FakePicker(string? result) : IFolderPicker
    {
        public Task<string?> PickAsync(string startFolder) => Task.FromResult(result);
    }

    private static InstallerViewModel Fresh(IInstallEngine engine, InstalledApp? installed = null, InstallOptions? existing = null)
        => new(engine, "LICENSE TEXT", installed, existing);

    [Fact]
    public async Task HappyPath_WelcomeLicenseOptionsInstallDoneLaunch()
    {
        var engine = new FakeEngine();
        var closed = false;
        var vm = Fresh(engine);
        vm.RequestClose = () => closed = true;

        Assert.Equal(InstallStep.Welcome, vm.Step);
        Assert.False(vm.IsInstalled);
        Assert.Equal("Next >", vm.PrimaryButtonText);
        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.Equal(InstallStep.License, vm.Step);
        Assert.False(vm.CanGoPrimary);
        vm.LicenseAccepted = true;
        Assert.True(vm.CanGoPrimary);
        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.Equal(InstallStep.Options, vm.Step);
        Assert.Equal("Install", vm.PrimaryButtonText);
        Assert.True(vm.CanChooseFolder);
        vm.DesktopShortcut = false;
        vm.StartWithWindows = true;
        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.Equal(InstallStep.Done, vm.Step);
        Assert.False(vm.Failed);
        Assert.True(vm.DoneInstalled);
        Assert.Equal("Finish", vm.PrimaryButtonText);
        Assert.NotNull(engine.Received);
        Assert.Equal(InstallFolderRules.DefaultFolder, engine.Received.InstallFolder);
        Assert.False(engine.Received.DesktopShortcut);
        Assert.True(engine.Received.StartWithWindows);
        Assert.True(engine.Received.CheckForUpdates);
        Assert.False(engine.Received.Reinstall);

        await vm.PrimaryCommand.ExecuteAsync(null);
        Assert.Equal(InstallFolderRules.DefaultFolder, engine.Launched);
        Assert.True(closed);
    }

    [Fact]
    public async Task Failure_ShowsMessageAndLogAndCloseButtonWithoutLaunching()
    {
        var engine = new FakeEngine { Outcome = new InstallOutcome(false, false, "It broke.", @"C:\log.txt") };
        var closed = false;
        var vm = Fresh(engine);
        vm.RequestClose = () => closed = true;
        vm.LicenseAccepted = true;
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
        var vm = Fresh(engine);
        vm.LicenseAccepted = true;
        vm.Step = InstallStep.Options;

        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.True(vm.Failed);
        Assert.Null(engine.Received);
        Assert.Contains("no package", vm.ErrorText);
    }

    [Fact]
    public void BadFolder_BlocksInstallAndExplains()
    {
        var vm = Fresh(new FakeEngine());
        vm.LicenseAccepted = true;
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
        var vm = Fresh(new FakeEngine());
        vm.FolderPicker = new FakePicker(@"D:\Apps");
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(@"D:\Apps\ThisIsMyPC", vm.InstallFolder);

        vm.FolderPicker = new FakePicker(null);
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(@"D:\Apps\ThisIsMyPC", vm.InstallFolder);
    }

    [Fact]
    public void ExistingChoices_SeedTheOptions()
    {
        var existing = new InstallOptions(@"C:\X\ThisIsMyPC", true, StartWithWindows: true, CheckForUpdates: false);
        var vm = Fresh(new FakeEngine(), existing: existing);
        Assert.False(vm.IsInstalled);
        Assert.True(vm.StartWithWindows);
        Assert.False(vm.CheckForUpdates);
        vm.Step = InstallStep.Options;
        Assert.Equal("Install", vm.PrimaryButtonText);
    }

    [Fact]
    public void OlderInstalled_UpdatesInPlaceAndLocksTheFolder()
    {
        var installed = new InstalledApp("0.0.9", @"D:\Apps\ThisIsMyPC", @"D:\Apps\ThisIsMyPC\Update.exe");
        var vm = Fresh(new FakeEngine(), installed);
        Assert.True(vm.IsInstalled);
        Assert.Equal(InstalledVersionRelation.Older, vm.VersionRelation);
        Assert.Contains(@"Version 0.0.9 is installed in D:\Apps\ThisIsMyPC", vm.InstalledSummary);
        Assert.Equal(@"D:\Apps\ThisIsMyPC", vm.InstallFolder);
        Assert.False(vm.CanChooseFolder);
        Assert.True(vm.CanGoPrimary);
        vm.Step = InstallStep.Options;
        Assert.Equal("Update", vm.PrimaryButtonText);
    }

    [Fact]
    public async Task SameVersionInstalled_ReinstallsWithTheReinstallFlag()
    {
        var engine = new FakeEngine();
        var installed = new InstalledApp(InstallerViewModel.AppVersion, InstallFolderRules.DefaultFolder, "x");
        var vm = Fresh(engine, installed);
        vm.LicenseAccepted = true;
        Assert.Equal(InstalledVersionRelation.Same, vm.VersionRelation);
        vm.Step = InstallStep.Options;
        Assert.Equal("Reinstall", vm.PrimaryButtonText);

        await vm.PrimaryCommand.ExecuteAsync(null);
        Assert.NotNull(engine.Received);
        Assert.True(engine.Received.Reinstall);
    }

    [Fact]
    public void NewerInstalled_BlocksNextAndOffersUninstall()
    {
        var installed = new InstalledApp("99.0.0", InstallFolderRules.DefaultFolder, "x");
        var vm = Fresh(new FakeEngine(), installed);
        Assert.Equal(InstalledVersionRelation.Newer, vm.VersionRelation);
        Assert.True(vm.InstallBlockedByNewer);
        Assert.False(vm.CanGoPrimary);
        Assert.Contains("newer than this installer", vm.InstalledSummary);
        Assert.True(vm.CanUninstall);
    }

    [Fact]
    public async Task Uninstall_ConfirmsThenRunsTheUninstallerAndReportsRemoved()
    {
        var engine = new FakeEngine();
        var installed = new InstalledApp("0.0.9", @"D:\Apps\ThisIsMyPC", @"D:\Apps\ThisIsMyPC\Update.exe");
        var closed = false;
        var vm = Fresh(engine, installed);
        vm.RequestClose = () => closed = true;

        vm.UninstallCommand.Execute(null);
        Assert.Equal(InstallStep.ConfirmUninstall, vm.Step);
        Assert.Equal("Remove", vm.PrimaryButtonText);
        Assert.True(vm.CanGoBack);
        Assert.Null(engine.Uninstalled);

        await vm.PrimaryCommand.ExecuteAsync(null);
        Assert.Equal(InstallStep.Done, vm.Step);
        Assert.Same(installed, engine.Uninstalled);
        Assert.True(vm.Removed);
        Assert.False(vm.Failed);
        Assert.False(vm.DoneInstalled);
        Assert.False(vm.IsInstalled);
        Assert.Equal("Removed", vm.StepCaption);
        Assert.Equal("Close", vm.PrimaryButtonText);

        await vm.PrimaryCommand.ExecuteAsync(null);
        Assert.Null(engine.Launched);
        Assert.True(closed);
    }

    [Fact]
    public async Task Uninstall_FailureKeepsTheInstallAndExplains()
    {
        var engine = new FakeEngine { UninstallOutcome = new InstallOutcome(false, false, "Update.exe missing", null) };
        var installed = new InstalledApp("0.0.9", @"D:\Apps\ThisIsMyPC", "x");
        var vm = Fresh(engine, installed);
        vm.Step = InstallStep.ConfirmUninstall;

        await vm.PrimaryCommand.ExecuteAsync(null);
        Assert.True(vm.Failed);
        Assert.True(vm.IsInstalled);
        Assert.Equal("Not removed", vm.StepCaption);
        Assert.Equal("Update.exe missing", vm.ErrorText);
    }

    [Fact]
    public void Back_WalksLicenseOptionsAndConfirmUninstall()
    {
        var vm = Fresh(new FakeEngine());
        Assert.False(vm.CanGoBack);
        vm.Step = InstallStep.Options;
        vm.BackCommand.Execute(null);
        Assert.Equal(InstallStep.License, vm.Step);
        vm.BackCommand.Execute(null);
        Assert.Equal(InstallStep.Welcome, vm.Step);
        Assert.False(vm.CanGoBack);

        vm.Step = InstallStep.ConfirmUninstall;
        vm.BackCommand.Execute(null);
        Assert.Equal(InstallStep.Welcome, vm.Step);
    }

    [Fact]
    public void Tabs_FollowTheStepAndOnlyOpenWhereTheButtonsWouldGo()
    {
        var vm = Fresh(new FakeEngine());
        Assert.Equal(InstallerViewModel.WelcomeTab, vm.TabIndex);
        Assert.True(vm.CanOpenLicense);
        Assert.False(vm.CanOpenOptions);

        vm.TabIndex = InstallerViewModel.OptionsTab;
        Assert.Equal(InstallStep.Welcome, vm.Step);

        vm.TabIndex = InstallerViewModel.LicenseTab;
        Assert.Equal(InstallStep.License, vm.Step);
        vm.LicenseAccepted = true;
        Assert.True(vm.CanOpenOptions);
        vm.TabIndex = InstallerViewModel.OptionsTab;
        Assert.Equal(InstallStep.Options, vm.Step);

        vm.TabIndex = InstallerViewModel.InstallTab;
        Assert.Equal(InstallStep.Options, vm.Step);
        vm.TabIndex = InstallerViewModel.WelcomeTab;
        Assert.Equal(InstallStep.Welcome, vm.Step);
    }

    [Fact]
    public async Task Tabs_FreezeOnceTheResultIsIn()
    {
        var vm = Fresh(new FakeEngine());
        vm.LicenseAccepted = true;
        vm.Step = InstallStep.Options;
        await vm.PrimaryCommand.ExecuteAsync(null);

        Assert.Equal(InstallerViewModel.InstallTab, vm.TabIndex);
        Assert.True(vm.IsInInstallTab);
        Assert.False(vm.CanOpenWelcome);
        Assert.False(vm.CanOpenOptions);
        vm.TabIndex = InstallerViewModel.WelcomeTab;
        Assert.Equal(InstallStep.Done, vm.Step);
    }

    [Fact]
    public void Tabs_RemoveTabShowsWhenInstalledAndHoldsTheRemoveFlow()
    {
        Assert.False(Fresh(new FakeEngine()).ShowRemoveTab);

        var installed = new InstalledApp("0.0.9", @"D:\Apps\ThisIsMyPC", "x");
        var vm = Fresh(new FakeEngine(), installed);
        Assert.True(vm.ShowRemoveTab);
        Assert.False(vm.IsInRemoveTab);

        vm.UninstallCommand.Execute(null);
        Assert.Equal(InstallerViewModel.RemoveTab, vm.TabIndex);
        Assert.True(vm.IsInRemoveTab);
        Assert.True(vm.CanOpenWelcome);
        vm.TabIndex = InstallerViewModel.WelcomeTab;
        Assert.Equal(InstallStep.Welcome, vm.Step);
    }

    [Theory]
    [InlineData(null, "1.0.0", InstalledVersionRelation.NotInstalled)]
    [InlineData("0.9.0", "1.0.0", InstalledVersionRelation.Older)]
    [InlineData("1.0.0", "1.0.0", InstalledVersionRelation.Same)]
    [InlineData("1.0.0.0", "1.0.0", InstalledVersionRelation.Same)]
    [InlineData("1.0.1", "1.0.0", InstalledVersionRelation.Newer)]
    [InlineData("1.0.0-beta.1", "1.0.0", InstalledVersionRelation.Same)]
    [InlineData("weird", "1.0.0", InstalledVersionRelation.Older)]
    public void Compare_OrdersVersions(string? installed, string package, InstalledVersionRelation expected)
    {
        Assert.Equal(expected, InstallerViewModel.Compare(installed, package));
    }
}
