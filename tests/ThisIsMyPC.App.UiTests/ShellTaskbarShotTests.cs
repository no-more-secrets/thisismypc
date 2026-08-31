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
    public void TaskbarSection_RendersTheChoiceRows()
    {
        var queue = new PendingChangesService();
        var viewModel = new ShellViewModel(CreateScanData(), queue, Registry);
        using var session = UiSession.ForView(new ShellView(), viewModel, "shell-taskbar", height: 1100);

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
