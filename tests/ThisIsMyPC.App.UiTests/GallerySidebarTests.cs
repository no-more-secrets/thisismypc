using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The UI Gallery sidebar entry is Debug-only. In Debug builds, clicking it must
/// open the gallery AND show the button in its active (selected) state; Release
/// builds hide the button entirely, so the click test skips itself there and the
/// hidden-state test asserts the inverse. Boots the real MainWindow, so
/// Category=Diagnostic.
/// </summary>
[Trait("Category", "Diagnostic")]
public class GallerySidebarTests
{
    [AvaloniaFact(Timeout = 60_000)]
    public async Task GalleryButton_MatchesBuildConfiguration()
    {
        using var session = UiSession.ForMainWindow("gallery-sidebar");
        var viewModel = (MainWindowViewModel)session.Window.DataContext!;

        await session.WaitForAsync(
            () => viewModel.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");

        if (!MainWindowViewModel.IsGalleryVisible)
        {
            // Release: the dev-facing entry must not be reachable at all.
            Assert.DoesNotContain("UI Gallery", session.DescribeVisibleText(), StringComparison.Ordinal);
            session.Screenshot("sidebar-release-no-gallery");
            return;
        }

        session.ClickText("UI Gallery");
        await session.WaitForAsync(
            () => viewModel.ContentTitle == "UI Gallery", timeoutMs: 30_000, what: "gallery load");

        // The regression: OpenGallery used to reset IsGalleryActive right after
        // setting it, leaving no sidebar item selected.
        Assert.True(viewModel.IsGalleryActive);
        session.Screenshot("sidebar-gallery-selected");
    }
}
