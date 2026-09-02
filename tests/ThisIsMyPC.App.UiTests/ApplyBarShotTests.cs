using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The apply bar with a change staged: the accent Apply button with its
/// rim and the count badge with its own. Stages one Explorer toggle and
/// flips it back; nothing is applied. Boots the real MainWindow, so
/// Category=Diagnostic.
/// </summary>
[Trait("Category", "Diagnostic")]
public class ApplyBarShotTests
{
    [AvaloniaFact(Timeout = 300_000)]
    public async Task ApplyButton_AndCountBadge_WithOneChangeStaged()
    {
        using var session = UiSession.ForMainWindow("apply-bar");
        var viewModel = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => viewModel.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");

        session.ClickText("Explorer");
        await session.WaitForAsync(
            () => viewModel.CurrentContent is ShellViewModel, timeoutMs: 120_000, what: "Explorer content load");
        session.Screenshot("explorer-no-changes");

        var first = session.Find<ToggleSwitch>(_ => true);
        session.Click(first);
        await session.WaitForAsync(() => viewModel.HasPendingChanges, timeoutMs: 10_000, what: "one staged change");
        session.Screenshot("explorer-one-pending");
        Assert.True(session.IsTextVisible("Apply"));
        Assert.True(session.IsTextVisible("1"));

        session.Click(first);
        await session.WaitForAsync(() => !viewModel.HasPendingChanges, timeoutMs: 10_000, what: "change unstaged");
    }
}
