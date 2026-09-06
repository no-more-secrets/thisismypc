using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The Review Pending Changes panel after an apply stopped inside a group: the
/// notice above the list says what to click, the stopped group carries its
/// note and rim, a clean group behind it looks unchanged, and every line fits
/// inside the panel in both themes. Uses the real queue against a fake apply
/// delegate; nothing touches the live system. CI-safe.
/// </summary>
public class ReviewPanelUnresolvedShotTests
{
    private static ChangeDescriptor Change(string id, string name, ChangeCategory category = ChangeCategory.Enable) => new()
    {
        ModuleId = "Explorer",
        SettingId = id,
        DisplayName = name,
        SystemLocation = $@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\{id}",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Off",
        AfterDisplay = "On",
        ValueType = ChangeValueType.Registry_DWord,
        Category = category,
    };

    private static ChangeGroup Group(string id, params ChangeDescriptor[] changes) => new()
    {
        GroupId = id,
        DisplayName = changes[0].DisplayName,
        Description = changes[0].DisplayName,
        Changes = changes,
    };

    /// <summary>Real queue: group 1 completes, group 2 fails on its second change, group 3 never runs.</summary>
    private static async Task<PendingChangesService> QueueWithOneUnresolvedGroupAsync()
    {
        var queue = new PendingChangesService();
        queue.Stage(Group("done", Change("hidden-files", "Show hidden files")));
        queue.Stage(Group("stopped",
            Change("classic-menu-a", "Classic right-click menu and its Windows 11 fallback entries", ChangeCategory.Disable),
            Change("classic-menu-b", "Classic right-click menu companion key", ChangeCategory.Disable)));
        queue.Stage(Group("waiting", Change("file-ext", "Show file name extensions")));

        var result = await queue.ApplyAllAsync(
            change => Task.FromResult(change.SettingId == "classic-menu-b"
                ? OperationResult<bool>.Failure("Access is denied. The key is owned by TrustedInstaller and the app could not take ownership of it", ErrorCategory.AccessDenied)
                : OperationResult<bool>.Success(true)),
            _ => Task.FromResult(OperationResult<bool>.Success(true)));

        Assert.False(result.IsSuccess);
        Assert.Single(queue.ReconciliationRequired);
        return queue;
    }

    [AvaloniaTheory]
    [InlineData("dark", 520, 620)]
    [InlineData("light", 520, 620)]
    [InlineData("dark-short", 520, 420)]
    public async Task UnresolvedGroup_ShowsNoticeAndNote_InsideThePanel(string variant, int width, int height)
    {
        var queue = await QueueWithOneUnresolvedGroupAsync();
        var writer = new CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-review-{Guid.NewGuid():N}"));
        var viewModel = new ReviewPanelViewModel(queue, writer);

        using var session = UiSession.ForView(new ReviewPanelView(), viewModel, "review-panel-unresolved", width, height);
        try
        {
            session.SetTheme(variant.StartsWith("light", StringComparison.Ordinal) ? ThemeVariant.Light : ThemeVariant.Dark);
            session.Screenshot(variant);

            Assert.True(viewModel.HasUnresolvedGroups);
            Assert.True(session.IsTextVisible("Needs your attention"));
            Assert.True(session.IsTextVisible("Discard All"));
            Assert.Equal(2, viewModel.ReviewGroups.Count);
            var stopped = Assert.Single(viewModel.ReviewGroups, g => g.NeedsReview);
            Assert.StartsWith("Did not finish at", stopped.ReviewNote, StringComparison.Ordinal);

            var panel = session.Find<Border>(b => b.Width == 450);
            var panelLeft = panel.TranslatePoint(new Avalonia.Point(0, 0), session.Window)!.Value.X;
            var panelRight = panelLeft + panel.Bounds.Width;

            // Every visible text block fits inside the panel: nothing is cut off at the edge.
            foreach (var text in session.FindAll<TextBlock>(t => !string.IsNullOrEmpty(t.Text)))
            {
                var left = text.TranslatePoint(new Avalonia.Point(0, 0), session.Window)!.Value.X;
                var right = left + text.Bounds.Width;
                Assert.True(right <= panelRight - 8, $"'{text.Text}' runs to {right:F0}, past the panel edge at {panelRight:F0}");
                Assert.True(left >= panelLeft + 8, $"'{text.Text}' starts at {left:F0}, before the panel edge at {panelLeft:F0}");
            }

            // The note and the notice keep the scrollbar lane (16px) like the rows do.
            var notice = session.Find<TextBlock>(t => t.Text == "Needs your attention");
            var noticeRight = notice.TranslatePoint(new Avalonia.Point(notice.Bounds.Width, 0), session.Window)!.Value.X;
            Assert.True(panelRight - noticeRight >= 16 + 12, $"notice ends {panelRight - noticeRight:F1}px from the panel edge");
        }
        finally
        {
            session.SetTheme(ThemeVariant.Dark);
        }
    }

    [AvaloniaFact]
    public async Task DiscardAll_ClearsTheNotice()
    {
        var queue = await QueueWithOneUnresolvedGroupAsync();
        var writer = new CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-review-{Guid.NewGuid():N}"));
        var viewModel = new ReviewPanelViewModel(queue, writer);
        using var session = UiSession.ForView(new ReviewPanelView(), viewModel, "review-panel-unresolved", 520, 620);
        Assert.True(session.IsTextVisible("Needs your attention"));

        queue.DiscardAll();
        session.Screenshot("after-discard");

        Assert.False(viewModel.HasUnresolvedGroups);
        Assert.False(session.IsTextVisible("Needs your attention"));
        Assert.True(session.IsTextVisible("No pending changes"));
    }
}
