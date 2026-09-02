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

    private const string License = "                    GNU GENERAL PUBLIC LICENSE\n                       Version 2, June 1991\n\n Copyright (C) 1989, 1991 Free Software Foundation, Inc.\n\n                            Preamble\n\n  The licenses for most software are designed to take away your\nfreedom to share and change it.";

    private static UiSession Open(InstallerViewModel viewModel)
        => UiSession.ForView(new InstallerView(), viewModel, "installer", width: 720, height: 640);

    /// <summary>The tab strip repeats some button words (Install, Remove); this clicks the footer button.</summary>
    private static void ClickButton(UiSession session, string text)
        => session.Click(session.Find<Avalonia.Controls.Button>(b => b.Content as string == text));

    [AvaloniaFact]
    public async Task Walkthrough_EveryPageRendersAndChoicesReachTheEngine()
    {
        var engine = new FakeEngine();
        var viewModel = new InstallerViewModel(engine, License, installed: null, existing: null);
        using var session = Open(viewModel);

        session.Screenshot("welcome");
        Assert.True(session.IsTextVisible("Next >"));
        Assert.False(session.IsTextVisible("< Back"));
        Assert.False(session.IsTextVisible("Uninstall"));

        // Hover states: the accent button changes its whole fill, the plain
        // button its fill and rim together. Inspect the PNGs.
        session.HoverText("Next >");
        session.Screenshot("welcome-hover-next");
        session.HoverText("Cancel");
        session.Screenshot("welcome-hover-cancel");

        session.ClickText("Next >");
        session.Screenshot("license");
        // The license lives in a TextBox, which renders as one text run, so
        // look for the checkbox label and the page caption instead.
        Assert.True(session.IsTextVisible("License"));
        Assert.True(session.IsTextVisible("I accept the terms of the GNU General Public License, version 2"));
        Assert.False(viewModel.CanGoPrimary);

        session.ClickText("I accept the terms of the GNU General Public License, version 2");
        Assert.True(viewModel.LicenseAccepted);
        session.ClickText("Next >");
        session.Screenshot("options");
        Assert.True(session.IsTextVisible("Install folder"));
        Assert.True(session.IsTextVisible("Install"));

        session.ClickText("Add a shortcut on the Desktop");
        Assert.False(viewModel.DesktopShortcut);
        session.ClickText("Start with Windows, in the tray");
        Assert.True(viewModel.StartWithWindows);

        // The tab strip walks back without the Back button, and forward only
        // as far as the buttons would allow.
        session.ClickText("Welcome");
        Assert.Equal(InstallStep.Welcome, viewModel.Step);
        session.Screenshot("tab-back-to-welcome");
        session.ClickText("Options");
        Assert.Equal(InstallStep.Options, viewModel.Step);
        session.ClickText("Install");
        Assert.Equal(InstallStep.Options, viewModel.Step);

        ClickButton(session, "Install");
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
        var viewModel = new InstallerViewModel(new FakeEngine(), License, installed: null, existing: null) { LicenseAccepted = true };
        viewModel.Step = InstallStep.Options;
        using var session = Open(viewModel);

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
    public async Task Installed_WelcomeNamesTheVersionAndUninstallWalksConfirmToRemoved()
    {
        var engine = new FakeEngine();
        var installed = new InstalledApp("0.0.9", @"C:\Program Files\NMS\ThisIsMyPC", @"C:\Program Files\NMS\ThisIsMyPC\Update.exe");
        var viewModel = new InstallerViewModel(engine, License, installed, existing: null);
        using var session = Open(viewModel);

        session.Screenshot("welcome-installed");
        Assert.True(session.IsTextVisible("Uninstall"));
        Assert.True(session.IsTextVisible(viewModel.InstalledSummary));

        session.ClickText("Uninstall");
        session.Screenshot("confirm-uninstall");
        Assert.True(session.IsTextVisible("Remove"));
        Assert.True(session.IsTextVisible("< Back"));
        Assert.Null(engine.Uninstalled);

        ClickButton(session, "Remove");
        await session.WaitForAsync(() => viewModel.Step == InstallStep.Done, what: "uninstall to finish");
        session.Screenshot("removed");
        Assert.Same(installed, engine.Uninstalled);
        Assert.True(session.IsTextVisible("ThisIsMyPC was removed. Your settings and change history are still in the ProgramData folder in case you install it again."));
        Assert.True(session.IsTextVisible("Close"));
        Assert.False(session.IsTextVisible("Finish"));
    }

    [AvaloniaFact]
    public void Installed_OptionsLocksTheFolderAndSaysUpdate()
    {
        var installed = new InstalledApp("0.0.9", @"C:\Program Files\NMS\ThisIsMyPC", "x");
        var viewModel = new InstallerViewModel(new FakeEngine(), License, installed, existing: null) { LicenseAccepted = true };
        viewModel.Step = InstallStep.Options;
        using var session = Open(viewModel);

        session.Screenshot("options-update");
        Assert.True(session.IsTextVisible("Updates go into the folder the app is already in."));
        Assert.True(session.IsTextVisible("Update"));
    }

    [AvaloniaFact]
    public void NewerInstalled_WelcomeBlocksNext()
    {
        var installed = new InstalledApp("99.0.0", @"C:\Program Files\NMS\ThisIsMyPC", "x");
        var viewModel = new InstallerViewModel(new FakeEngine(), License, installed, existing: null);
        using var session = Open(viewModel);

        session.Screenshot("welcome-newer-installed");
        Assert.False(viewModel.CanGoPrimary);
        Assert.True(session.IsTextVisible("Uninstall"));
    }

    [AvaloniaFact]
    public async Task Done_FailureShowsMessageAndLogPath()
    {
        var engine = new FakeEngine
        {
            Outcome = new InstallOutcome(false, false,
                "This version of ThisIsMyPC is already installed. Run this installer again and choose Uninstall on the first page, or remove it from Settings, Apps, Installed apps.",
                @"C:\ProgramData\ThisIsMyPC\logs\install-20260901-210000.log"),
        };
        var viewModel = new InstallerViewModel(engine, License, installed: null, existing: null) { LicenseAccepted = true };
        viewModel.Step = InstallStep.Options;
        using var session = Open(viewModel);

        ClickButton(session, "Install");
        await session.WaitForAsync(() => viewModel.Step == InstallStep.Done, what: "install to finish");
        session.Screenshot("done-failed");
        Assert.True(session.IsTextVisible("Close"));
        Assert.True(session.IsTextVisible(engine.Outcome.Error!));
        Assert.False(session.IsTextVisible("Finish"));
    }
}
