using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// Hover-state audit: parks the mouse on the app's interactive elements and
/// screenshots each, in both themes, so hover styling can be judged from
/// pixels. Boots the real MainWindow, so Category=Diagnostic.
/// </summary>
[Trait("Category", "Diagnostic")]
public class HoverAuditTests
{
    [AvaloniaFact(Timeout = 300_000)]
    public async Task HoverKeyElementsInBothThemes()
    {
        using var session = UiSession.ForMainWindow("hover-audit");
        var viewModel = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(
            () => viewModel.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");

        foreach (var (variant, suffix) in new[]
        {
            (ThemeVariant.Dark, "dark"),
            (ThemeVariant.Light, "light"),
        })
        {
            session.SetTheme(variant);

            // The sidebar folds every group but the open module's, so open
            // Explorer's group first and hover a sibling that is not active.
            session.OpenModule("Explorer");
            await session.WaitForAsync(
                () => viewModel.ContentTitle == "Explorer", timeoutMs: 120_000, what: "explorer load");
            session.HoverText("Environment");
            session.Screenshot($"sidebar-item-{suffix}");

            session.HoverText("Presets");
            session.Screenshot($"sidebar-presets-{suffix}");

            session.HoverText("History");
            session.Screenshot($"applybar-history-{suffix}");

            session.HoverText("Apply");
            session.Screenshot($"applybar-apply-{suffix}");

            session.OpenModule("Windows Annoyances");
            await session.WaitForAsync(
                () => viewModel.ContentTitle == "Windows Annoyances",
                timeoutMs: 60_000, what: "annoyances load");
            session.HoverText("Technical details");
            session.Screenshot($"card-toolbar-{suffix}");

            var toggle = session.TryFind<ToggleSwitch>(_ => true);
            if (toggle is not null)
            {
                session.Hover(toggle);
                session.Screenshot($"toggle-{suffix}");
            }
        }
    }
}
