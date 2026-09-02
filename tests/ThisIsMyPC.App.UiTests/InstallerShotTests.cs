using Avalonia.Headless.XUnit;
using ThisIsMyPC.Installer.Services;
using ThisIsMyPC.Installer.ViewModels;
using ThisIsMyPC.Installer.Views;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The installer's pages, rendered the way a person walks them: click Next,
/// tick the license, untick a shortcut, click Install. A fake engine stands
/// in for msiexec, so this is CI-safe and installs nothing.
/// </summary>
public class InstallerShotTests
{
    private sealed class FakeEngine : IInstallEngine
    {
        public bool HasPackage => true;
        public InstallOptions? Received { get; private set; }
        public InstallOutcome Outcome { get; set; } = new(true, false, null, @"C:\ProgramData\ThisIsMyPC\logs\install.log");

        public Task<InstallOutcome> InstallAsync(InstallOptions options, IProgress<string> progress, CancellationToken cancellationToken)
        {
            Received = options;
            return Task.FromResult(Outcome);
        }

        public void Launch(string installFolder) { }
    }

    private const string License = "                    GNU GENERAL PUBLIC LICENSE\n                       Version 2, June 1991\n\n Copyright (C) 1989, 1991 Free Software Foundation, Inc.\n\n                            Preamble\n\n  The licenses for most software are designed to take away your\nfreedom to share and change it.";

    [AvaloniaFact]
    public async Task Walkthrough_EveryPageRendersAndChoicesReachTheEngine()
    {
        var engine = new FakeEngine();
        var viewModel = new InstallerViewModel(engine, License, existing: null);
        using var session = UiSession.ForView(new InstallerView(), viewModel, "installer", width: 720, height: 600);

        session.Screenshot("welcome");
        Assert.True(session.IsTextVisible("Next"));
        Assert.False(session.IsTextVisible("Back"));

        session.ClickText("Next");
        session.Screenshot("license");
        // The license lives in a TextBox, which renders as one text run, so
        // look for the checkbox label and the page caption instead.
        Assert.True(session.IsTextVisible("License"));
        Assert.True(session.IsTextVisible("I accept the terms of the GNU General Public License, version 2"));
        Assert.False(viewModel.CanGoPrimary);

        session.ClickText("I accept the terms of the GNU General Public License, version 2");
        Assert.True(viewModel.LicenseAccepted);
        session.ClickText("Next");
        session.Screenshot("options");
        Assert.True(session.IsTextVisible("Install folder"));
        Assert.True(session.IsTextVisible("Install"));

        session.ClickText("Add a shortcut on the Desktop");
        Assert.False(viewModel.DesktopShortcut);
        session.ClickText("Start with Windows, in the tray");
        Assert.True(viewModel.StartWithWindows);

        session.ClickText("Install");
        await session.WaitForAsync(() => viewModel.Step == InstallStep.Done, what: "install to finish");
        session.Screenshot("done");
        Assert.True(session.IsTextVisible("Finish"));
        Assert.True(session.IsTextVisible("Open ThisIsMyPC when I click Finish"));

        Assert.NotNull(engine.Received);
        Assert.False(engine.Received.DesktopShortcut);
        Assert.True(engine.Received.StartWithWindows);
        Assert.True(engine.Received.CheckForUpdates);
        Assert.Equal(InstallFolderRules.DefaultFolder, engine.Received.InstallFolder);
    }

    [AvaloniaFact]
    public void Options_BadFolderShowsTheReasonAndDisablesInstall()
    {
        var viewModel = new InstallerViewModel(new FakeEngine(), License, existing: null) { LicenseAccepted = true };
        viewModel.Step = InstallStep.Options;
        using var session = UiSession.ForView(new InstallerView(), viewModel, "installer", width: 720, height: 600);

        viewModel.InstallFolder = @"D:\Apps\ThisIsMyPC";
        session.Pump();
        session.Screenshot("options-outside-program-files");
        Assert.True(session.IsTextVisible("This folder is outside Program Files, so other programs on this PC can change the files in it. ThisIsMyPC will warn about that every time it starts."));

        viewModel.InstallFolder = @"C:\";
        session.Pump();
        session.Screenshot("options-drive-root");
        Assert.True(session.IsTextVisible("Pick a folder, not the whole drive."));
        Assert.False(viewModel.CanGoPrimary);
    }

    [AvaloniaFact]
    public async Task Done_FailureShowsMessageAndLogPath()
    {
        var engine = new FakeEngine
        {
            Outcome = new InstallOutcome(false, false,
                "This version of ThisIsMyPC is already installed. To install it again, remove it first: Settings, Apps, Installed apps.",
                @"C:\ProgramData\ThisIsMyPC\logs\install-20260901-210000.log"),
        };
        var viewModel = new InstallerViewModel(engine, License, existing: null) { LicenseAccepted = true };
        viewModel.Step = InstallStep.Options;
        using var session = UiSession.ForView(new InstallerView(), viewModel, "installer", width: 720, height: 600);

        session.ClickText("Install");
        await session.WaitForAsync(() => viewModel.Step == InstallStep.Done, what: "install to finish");
        session.Screenshot("done-failed");
        Assert.True(session.IsTextVisible("Close"));
        Assert.True(session.IsTextVisible("Not installed"));
        Assert.False(session.IsTextVisible("Finish"));
    }
}
