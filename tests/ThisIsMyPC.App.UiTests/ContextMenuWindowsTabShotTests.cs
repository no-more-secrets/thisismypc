using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// Renders the curated Windows entries tab with an empty handler scan and the
/// real registry. Live-state reads make this Category=Diagnostic per CLAUDE.md
/// (never in CI); it stages only, never applies.
/// </summary>
[Trait("Category", "Diagnostic")]
public class ContextMenuWindowsTabShotTests
{
    [AvaloniaFact]
    public void WindowsTab_RendersCatalogAndTogglingStagesAGroup()
    {
        var queue = new PendingChangesService();
        var registry = new ThisIsMyPC.Interop.Win32.Registry.RegistryService();
        var viewModel = new ContextMenuViewModel([], queue, registry);
        using var session = UiSession.ForView(
            new ContextMenuView(), viewModel, "context-menu-windows", height: 1200);

        session.ClickText("Windows");
        session.Screenshot("windows-entries-tab");

        Assert.True(session.IsTextVisible("Extract all on MSI installers"));
        Assert.True(session.IsTextVisible("New Compressed folder"));
        Assert.True(session.IsTextVisible("Search the Microsoft Store in Open with"));

        // Flip the MSI Extract row to the opposite of its live state; the
        // debounced toggle stages exactly one group.
        var row = viewModel.WindowsEntries.First(e => e.Label == "Extract all on MSI installers");
        row.IsEnabled = !row.IsEnabled;
        session.Pump();
        Thread.Sleep(400); // toggle staging is debounced by 250 ms
        session.Pump();
        session.Screenshot("after-toggle");

        Assert.Equal(1, queue.PendingCount);

        row.IsEnabled = !row.IsEnabled;
        session.Pump();
        Thread.Sleep(400);
        session.Pump();
        Assert.Equal(0, queue.PendingCount);
    }
}
