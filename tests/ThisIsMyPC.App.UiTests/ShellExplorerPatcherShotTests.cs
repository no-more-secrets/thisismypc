using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The ExplorerPatcher settings block on each Explorer tab. CI-safe: an
/// in-memory registry backs the rows, so nothing reads or writes the machine.
/// </summary>
public class ShellExplorerPatcherShotTests
{
    private static readonly TaskbarSettings Taskbar = new(1, true, false, false);

    private static ShellScanData ScanData(bool patcherInstalled, string installedVersion = "") => new(
        ExplorerPreferences: [],
        Taskbar: Taskbar,
        ExplorerPatcherSettings: patcherInstalled
            ? [.. ExplorerPatcherCatalog.Entries.Select(e => e with { IsAvailable = e.Condition.Length == 0 })]
            : [],
        ExplorerPatcherInstalled: patcherInstalled,
        ExplorerPatcherVersion: installedVersion.Length > 0 ? installedVersion : ExplorerPatcherCatalog.Version,
        ExplorerPatcherCatalogVersion: ExplorerPatcherCatalog.Version);

    [AvaloniaFact]
    public void AnExplorerPatcherOtherThanThePinnedOne_SaysSoAboveTheRows()
    {
        var queue = new PendingChangesService();
        var viewModel = new ShellViewModel(
            ScanData(patcherInstalled: true, installedVersion: "99999.1.2.3"), queue, new UiFakeRegistryService());
        using var session = UiSession.ForView(new ShellView(), viewModel, "shell-explorerpatcher", height: 1400);

        session.ClickText("Taskbar");
        session.Screenshot("patcher-version-mismatch");

        Assert.True(viewModel.ShowPatcherVersionNote);
        Assert.Contains(ExplorerPatcherCatalog.Version, viewModel.PatcherVersionNote, StringComparison.Ordinal);
        Assert.Contains("99999.1.2.3", viewModel.PatcherVersionNote, StringComparison.Ordinal);
        Assert.True(session.IsTextVisible(viewModel.PatcherVersionNote));
    }

    [AvaloniaFact]
    public void ThePinnedExplorerPatcher_ShowsNoVersionNote()
    {
        var queue = new PendingChangesService();
        var viewModel = new ShellViewModel(ScanData(patcherInstalled: true), queue, new UiFakeRegistryService());

        Assert.False(viewModel.ShowPatcherVersionNote);
        Assert.Equal(string.Empty, viewModel.PatcherVersionNote);
    }

    [AvaloniaFact]
    public void WithExplorerPatcherInstalled_EveryTabShowsItsSettings()
    {
        var queue = new PendingChangesService();
        var viewModel = new ShellViewModel(ScanData(patcherInstalled: true), queue, new UiFakeRegistryService());
        using var session = UiSession.ForView(new ShellView(), viewModel, "shell-explorerpatcher", height: 1400);

        foreach (var tab in new[] { "General", "File Explorer", "Taskbar", "Desktop", "Start Menu" })
        {
            session.ClickText(tab);
            session.Screenshot("patcher-" + tab.ToLowerInvariant().Replace(' ', '-'));
            Assert.True(session.IsTextVisible("ExplorerPatcher"), $"no ExplorerPatcher heading on the {tab} tab");
        }

        Assert.True(viewModel.ShowPatcherSettings);
        Assert.True(viewModel.ShowGeneralPatcher);
        Assert.True(viewModel.ShowFileExplorerPatcher);
        Assert.True(viewModel.ShowTaskbarPatcher);
        Assert.True(viewModel.ShowDesktopPatcher);
        Assert.True(viewModel.ShowStartMenuPatcher);
    }

    [AvaloniaFact]
    public void WithoutExplorerPatcher_NoneOfItsRowsAppear()
    {
        var queue = new PendingChangesService();
        var viewModel = new ShellViewModel(ScanData(patcherInstalled: false), queue, new UiFakeRegistryService());
        using var session = UiSession.ForView(new ShellView(), viewModel, "shell-explorerpatcher", height: 1400);

        session.Screenshot("patcher-absent");

        Assert.False(viewModel.ShowPatcherSettings);
        Assert.Empty(viewModel.PatcherToggles);
        Assert.Empty(viewModel.PatcherChoices);
        Assert.False(session.IsTextVisible("ExplorerPatcher"));
    }

    [AvaloniaFact]
    public void WithoutExplorerPatcher_TheGeneralTabStillOffersTheInstaller()
    {
        var queue = new PendingChangesService();
        var actions = new PendingActionsService();
        var viewModel = new ShellViewModel(ScanData(patcherInstalled: false), queue, new UiFakeRegistryService(), actions);
        using var session = UiSession.ForView(new ShellView(), viewModel, "shell-explorerpatcher", height: 1400);

        session.Screenshot("patcher-absent-installer-card");

        Assert.True(viewModel.ShowGeneralPatcher);
        Assert.False(viewModel.ShowTaskbarPatcher);
        Assert.True(session.IsTextVisible("ExplorerPatcher"));
        Assert.True(session.IsTextVisible("valinet.ExplorerPatcher"));
        Assert.Equal("Install", viewModel.ExplorerPatcher!.ActionButtonText);
    }

    [AvaloniaFact]
    public void RowsSitUnderExplorerPatchersOwnPageNames()
    {
        // Sam: "Note ExplorerPatcher's actual tabs". Its pages become the
        // sub-headings, so "Theme" or "Row height" read in context.
        var queue = new PendingChangesService();
        var viewModel = new ShellViewModel(ScanData(patcherInstalled: true), queue, new UiFakeRegistryService());
        using var session = UiSession.ForView(new ShellView(), viewModel, "shell-explorerpatcher", height: 1600);

        session.ClickText("Taskbar");
        session.Screenshot("patcher-taskbar-groups");
        Assert.True(session.IsTextVisible("System tray"));

        session.ClickText("Desktop");
        session.Screenshot("patcher-desktop-groups");
        Assert.True(session.IsTextVisible("Window switcher (Alt+Tab)"));

        Assert.Equal(["", "System tray"], viewModel.TaskbarPatcherGroups.Select(g => g.Heading));
        Assert.Equal(["", "Window switcher (Alt+Tab)"], viewModel.DesktopPatcherGroups.Select(g => g.Heading));
        Assert.Equal(["", "Control Panel", "Updates", "Advanced"], viewModel.GeneralPatcherGroups.Select(g => g.Heading));
        Assert.Equal([""], viewModel.FileExplorerPatcherGroups.Select(g => g.Heading));
        // Rows keep the manifest's order: the taskbar style choice opens the Taskbar run.
        Assert.IsType<ShellChoiceSettingViewModel>(viewModel.TaskbarPatcherGroups[0].Rows[0]);
        Assert.IsType<ShellSettingViewModel>(viewModel.TaskbarPatcherGroups[0].Rows[^1]);
    }

    [AvaloniaFact]
    public void EveryRowSaysWhatItDoes()
    {
        var queue = new PendingChangesService();
        var viewModel = new ShellViewModel(ScanData(patcherInstalled: true), queue, new UiFakeRegistryService());

        Assert.All(viewModel.PatcherToggles, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Description), row.Label);
            Assert.False(row.Description.StartsWith("ExplorerPatcher,", StringComparison.Ordinal), row.Label);
        });
        Assert.All(viewModel.PatcherChoices, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Description), row.Label);
            Assert.False(row.Description.StartsWith("ExplorerPatcher,", StringComparison.Ordinal), row.Label);
        });
        // The General tab carries no rows about ExplorerPatcher's own debugging
        // or update channel, only its update policy (which holds the pin).
        var general = viewModel.GeneralPatcherGroups.SelectMany(g => g.Rows).ToList();
        Assert.DoesNotContain(general, r => r is ShellSettingViewModel t && t.Label.Contains("console", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(general, r => r is ShellSettingViewModel t && t.Label.Contains("memory", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(general, r => r is ShellSettingViewModel t && t.Label.Contains("pre-release", StringComparison.OrdinalIgnoreCase));
        Assert.Single(viewModel.GeneralPatcherGroups.First(g => g.Heading == "Updates").Rows);
    }

    [AvaloniaFact]
    public void SearchHidesAGroupHeadingWhenNoneOfItsRowsMatch()
    {
        var queue = new PendingChangesService();
        var viewModel = new ShellViewModel(ScanData(patcherInstalled: true), queue, new UiFakeRegistryService());
        using var session = UiSession.ForView(new ShellView(), viewModel, "shell-explorerpatcher", height: 1400);

        viewModel.SearchText = "Control Panel";
        session.Screenshot("patcher-search-control-panel");

        var general = viewModel.GeneralPatcherGroups;
        Assert.False(general.First(g => g.Heading == "").IsSearchVisible);
        Assert.True(general.First(g => g.Heading == "Control Panel").IsSearchVisible);
        Assert.True(session.IsTextVisible("Control Panel"));
        Assert.False(session.IsTextVisible("Updates"));

        viewModel.SearchText = string.Empty;
        Assert.All(general, g => Assert.True(g.IsSearchVisible));
    }

    [AvaloniaFact]
    public async Task TogglingAnExplorerPatcherRow_StagesAReversibleChange()
    {
        var registry = new UiFakeRegistryService();
        var queue = new PendingChangesService();
        var viewModel = new ShellViewModel(ScanData(patcherInstalled: true), queue, registry);
        using var session = UiSession.ForView(new ShellView(), viewModel, "shell-explorerpatcher", height: 1400);
        session.ClickText("Taskbar");

        var row = viewModel.PatcherToggles.First(r => r.Label.StartsWith("Skin taskbar", StringComparison.Ordinal));
        row.IsEnabled = !row.IsEnabled;
        await session.WaitForAsync(() => queue.PendingCount == 1, timeoutMs: 5000, what: "ExplorerPatcher staging");
        session.Screenshot("patcher-row-staged");

        var change = Assert.Single(Assert.Single(queue.PendingGroups).Changes);
        Assert.Equal("Explorer", change.ModuleId);
        Assert.StartsWith("explorerpatcher:", change.SettingId, StringComparison.Ordinal);
        Assert.Contains(@"Software\ExplorerPatcher\SkinMenus", change.SystemLocation, StringComparison.OrdinalIgnoreCase);
        // Nothing was written; staging is read-only until Apply.
        Assert.False(registry.ValueExists(ExplorerPatcherSettingsReader.ExplorerPatcherKeyPath, "SkinMenus").Value);

        row.IsEnabled = !row.IsEnabled;
        await session.WaitForAsync(() => queue.PendingCount == 0, timeoutMs: 5000, what: "ExplorerPatcher unstaging");
    }

    [AvaloniaFact]
    public void ChoiceRowsCarryExplorerPatchersOwnOptions()
    {
        var queue = new PendingChangesService();
        var viewModel = new ShellViewModel(ScanData(patcherInstalled: true), queue, new UiFakeRegistryService());

        var taskbarStyle = viewModel.PatcherChoices.First(r => r.SystemPath.EndsWith("OldTaskbar", StringComparison.Ordinal));

        Assert.Equal(3, taskbarStyle.Options.Count);
        Assert.Contains(taskbarStyle.Options, o => o.DisplayName.Contains("Windows 10", StringComparison.Ordinal));
        Assert.NotNull(taskbarStyle.SelectedOption);
    }
}
