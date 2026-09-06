using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The real window after an apply stops inside a group: the page is greyed
/// out, the review panel says what to click, and Discard All brings the page
/// back. The staged change targets the sample module, which does not exist,
/// so Apply fails before anything is written and nothing on the PC changes.
/// Boots the real MainWindow, so Category=Diagnostic.
/// </summary>
[Trait("Category", "Diagnostic")]
public class UnresolvedContentShotTests
{
    [AvaloniaFact(Timeout = 120_000)]
    public async Task UnresolvedGroup_DisablesThePage_UntilDiscardAll()
    {
        using var session = UiSession.ForMainWindow("unresolved-content");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0, what: "sidebar");
        session.ClickText("Environment");
        await session.WaitForAsync(() => vm.CurrentContent is EnvironmentViewModel, timeoutMs: 60_000, what: "Environment page");
        var content = session.Find<ContentControl>(c => c.Content == vm.CurrentContent);
        Assert.True(content.IsEffectivelyEnabled);

        vm.StageDebugChange(ChangeCategory.Enable);
        await session.WaitForAsync(() => vm.HasPendingChanges, what: "sample change staged");
        vm.IsReviewPanelOpen = true;
        await vm.ApplyAllCommand.ExecuteAsync(null);
        session.Pump();

        Assert.True(vm.HasUnresolvedGroups);
        Assert.False(vm.IsContentInteractive);
        Assert.False(content.IsEffectivelyEnabled);
        Assert.True(session.IsTextVisible("Needs your attention"));
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot($"page-disabled-{theme.Key}");
        }
        session.SetTheme(ThemeVariant.Dark);

        // A second sample change is refused while the group is unresolved.
        vm.StageDebugChange(ChangeCategory.Disable);
        Assert.Equal(1, vm.PendingCount);
        Assert.Equal(MainWindowViewModel.StagingBlockedMessage, vm.StatusMessage);

        session.ClickText("Discard All");
        await session.WaitForAsync(() => !vm.HasUnresolvedGroups && vm.IsContentInteractive, what: "discard");
        session.Pump();
        Assert.True(session.Find<ContentControl>(c => c.Content == vm.CurrentContent).IsEffectivelyEnabled);
        session.Screenshot("page-enabled-again");
    }
}
