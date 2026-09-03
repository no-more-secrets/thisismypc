using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// Taskbar section of the Explorer page, including the multi-choice rows.
/// Uses the real RegistryService for live taskbar state (Category=Diagnostic
/// per CLAUDE.md, never in CI); it stages only, never applies.
/// </summary>
[Trait("Category", "Diagnostic")]
public class ShellTaskbarShotTests
{
    private static readonly IRegistryService Registry =
        new ThisIsMyPC.Interop.Win32.Registry.RegistryService();

    private static ShellScanData CreateScanData() => new(
        ExplorerPreferences: [],
        Taskbar: new TaskbarSettingsReader(Registry).Read());

    [AvaloniaFact]
    public void ExplorerPage_HasFiveTabs_AndTheGeneralTabOffersExplorerPatcher()
    {
        var queue = new PendingChangesService();
        var actions = new PendingActionsService();
        var scan = new ShellScanData(new ExplorerSettingsReader(Registry).ReadAll(), new TaskbarSettingsReader(Registry).Read());
        var viewModel = new ShellViewModel(scan, queue, Registry, actions);
        using var session = UiSession.ForView(new ShellView(), viewModel, "shell-tabs", height: 1100);

        foreach (var tab in new[] { "General", "File Explorer", "Taskbar", "Desktop", "Start Menu" })
        {
            session.ClickText(tab);
            session.Screenshot(tab.ToLowerInvariant().Replace(' ', '-'));
        }

        Assert.NotEmpty(viewModel.GeneralSettings);
        Assert.NotEmpty(viewModel.DesktopSettings);
        Assert.NotEmpty(viewModel.StartMenuSettings);
        // The installer card opens the ExplorerPatcher block on General; the
        // other tabs carry only its settings, so it is not a Start Menu thing.
        Assert.False(session.IsTextVisible("valinet.ExplorerPatcher"), "the card is still on the Start Menu tab");
        session.ClickText("General");
        Assert.True(session.IsTextVisible("ExplorerPatcher"));
        Assert.True(session.IsTextVisible("valinet.ExplorerPatcher"));
        Assert.NotNull(viewModel.ExplorerPatcher);

        // Install queues the one-way action; a second press takes it back.
        var button = session.Find<Avalonia.Controls.Button>(b => ReferenceEquals(b.DataContext, viewModel.ExplorerPatcher));
        session.Click(button);
        session.Screenshot("explorerpatcher-queued");
        Assert.Equal(1, actions.PendingCount);
        Assert.Equal("Queued", viewModel.ExplorerPatcher!.ActionButtonText);
        session.Click(button);
        Assert.Equal(0, actions.PendingCount);
    }

    [AvaloniaFact]
    public void TaskbarSection_RendersTheChoiceRows()
    {
        var queue = new PendingChangesService();
        var viewModel = new ShellViewModel(CreateScanData(), queue, Registry);
        using var session = UiSession.ForView(new ShellView(), viewModel, "shell-taskbar", height: 1100);

        session.ClickText("Taskbar");
        session.Screenshot("taskbar-choice-rows");

        Assert.True(session.IsTextVisible("Taskbar search"));
        Assert.True(session.IsTextVisible("Combine taskbar buttons"));
    }

    [AvaloniaFact]
    public async Task ChoosingADifferentSearchMode_StagesAModifyChange()
    {
        var queue = new PendingChangesService();
        var viewModel = new ShellViewModel(CreateScanData(), queue, Registry);
        using var session = UiSession.ForView(new ShellView(), viewModel, "shell-taskbar", height: 1100);

        var search = viewModel.TaskbarChoiceSettings[0];
        var liveValue = search.SelectedOption!.Value;
        search.SelectedOption = search.Options.First(o => o.Value != liveValue);

        for (var i = 0; i < 200 && queue.PendingGroups.Count == 0; i++)
        {
            session.Pump();
            await Task.Delay(10);
        }

        var change = Assert.Single(Assert.Single(queue.PendingGroups).Changes);
        Assert.Equal("taskbar-search-mode", change.SettingId);
        Assert.Equal(liveValue.ToString(), change.BeforeValue);

        session.Screenshot("taskbar-choice-pending");
        queue.DiscardAll();
    }
}
