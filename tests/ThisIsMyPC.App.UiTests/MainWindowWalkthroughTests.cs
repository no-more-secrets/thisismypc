using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The full click-through: boots the real MainWindow on the real service graph
/// (fake winget/restore points, temp data paths), then visits every sidebar
/// module the way a person would and screenshots each page into
/// artifacts/ui-shots/walkthrough/. Module scans read the live system, so this
/// is Category=Diagnostic — run on demand, not in CI.
/// </summary>
[Trait("Category", "Diagnostic")]
public class MainWindowWalkthroughTests
{
    [AvaloniaFact(Timeout = 300_000)]
    public async Task VisitEveryModuleAndScreenshot()
    {
        using var session = UiSession.ForMainWindow("walkthrough");
        var viewModel = (MainWindowViewModel)session.Window.DataContext!;

        await session.WaitForAsync(
            () => viewModel.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");
        session.Screenshot("home");

        var moduleNames = viewModel.SidebarGroups
            .SelectMany(g => g.Items)
            .Where(i => i.IsAvailable)
            .Select(i => i.Name)
            .ToList();
        Assert.NotEmpty(moduleNames);

        foreach (var name in moduleNames)
        {
            session.ClickText(name);
            await session.WaitForAsync(
                () => viewModel.CurrentContent is not null && viewModel.ContentTitle == name,
                timeoutMs: 120_000,
                what: $"'{name}' content load");
            session.Screenshot(Slug(name));
        }
    }

    /// <summary>Same walkthrough in the Light variant, into walkthrough-light/.</summary>
    [AvaloniaFact(Timeout = 300_000)]
    public async Task VisitEveryModuleInLightTheme()
    {
        using var session = UiSession.ForMainWindow("walkthrough-light");
        var viewModel = (MainWindowViewModel)session.Window.DataContext!;

        try
        {
            session.SetTheme(Avalonia.Styling.ThemeVariant.Light);
            await session.WaitForAsync(
                () => viewModel.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");
            session.Screenshot("home");

            var moduleNames = viewModel.SidebarGroups
                .SelectMany(g => g.Items)
                .Where(i => i.IsAvailable)
                .Select(i => i.Name)
                .ToList();
            Assert.NotEmpty(moduleNames);

            foreach (var name in moduleNames)
            {
                session.ClickText(name);
                await session.WaitForAsync(
                    () => viewModel.CurrentContent is not null && viewModel.ContentTitle == name,
                    timeoutMs: 120_000,
                    what: $"'{name}' content load");
                session.Screenshot(Slug(name));
            }

            session.ClickText("Settings");
            await session.WaitForAsync(
                () => viewModel.ContentTitle == "Settings", timeoutMs: 30_000, what: "settings load");
            session.Screenshot("settings");
        }
        finally
        {
            session.SetTheme(Avalonia.Styling.ThemeVariant.Dark);
        }
    }

    private static string Slug(string name) =>
        new(name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
}
