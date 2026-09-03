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

    private static ShellScanData ScanData(bool patcherInstalled) => new(
        ExplorerPreferences: [],
        Taskbar: Taskbar,
        ExplorerPatcherSettings: patcherInstalled
            ? [.. ExplorerPatcherCatalog.Entries.Select(e => e with { IsAvailable = e.Condition.Length == 0 })]
            : [],
        ExplorerPatcherInstalled: patcherInstalled);

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
        Assert.Empty(viewModel.TaskbarPatcherToggles);
        Assert.Empty(viewModel.TaskbarPatcherChoices);
        Assert.False(session.IsTextVisible("ExplorerPatcher"));
    }

    [AvaloniaFact]
    public async Task TogglingAnExplorerPatcherRow_StagesAReversibleChange()
    {
        var registry = new UiFakeRegistryService();
        var queue = new PendingChangesService();
        var viewModel = new ShellViewModel(ScanData(patcherInstalled: true), queue, registry);
        using var session = UiSession.ForView(new ShellView(), viewModel, "shell-explorerpatcher", height: 1400);
        session.ClickText("Taskbar");

        var row = viewModel.TaskbarPatcherToggles.First(r => r.Label.StartsWith("Skin taskbar", StringComparison.Ordinal));
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

        var taskbarStyle = viewModel.TaskbarPatcherChoices.First(r => r.SystemPath.EndsWith("OldTaskbar", StringComparison.Ordinal));

        Assert.Equal(3, taskbarStyle.Options.Count);
        Assert.Contains(taskbarStyle.Options, o => o.DisplayName.Contains("Windows 10", StringComparison.Ordinal));
        Assert.NotNull(taskbarStyle.SelectedOption);
    }
}
