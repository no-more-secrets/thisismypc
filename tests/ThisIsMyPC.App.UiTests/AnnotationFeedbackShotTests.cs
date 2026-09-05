using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

[Trait("Category", "Diagnostic")]
public class AnnotationFeedbackShotTests
{
    [AvaloniaFact(Timeout = 180_000)]
    public async Task CaptureAnnotatedPagesAtReportedWidth()
    {
        using var session = UiSession.ForMainWindow("annotation-feedback");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        session.Window.Width = 1196;
        session.Window.Height = 800;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0,
            timeoutMs: 30_000, what: "sidebar population");
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            foreach (var page in new[] { "Home", "Explorer", "Presets", "Settings" })
            {
                session.ClickText(page);
                await session.WaitForAsync(() => vm.ContentTitle == page,
                    timeoutMs: 60_000, what: page);
                session.Pump();
                if (page == "Home" && session.IsTextVisible("Dismiss"))
                    session.ClickText("Dismiss");
                session.Screenshot($"{theme.Key}-{page}");
                if (page == "Presets")
                {
                    var card = session.Find<Button>(b => b.Classes.Contains("set-card"));
                    session.Click(card);
                    session.Screenshot($"{theme.Key}-preset-selected");
                }
                if (page == "Settings")
                {
                    var tabs = session.Window.GetVisualDescendants().OfType<TabItem>().Where(t => t.IsVisible).ToArray();
                    foreach (var tab in tabs)
                    {
                        session.Click(tab);
                        session.Screenshot($"{theme.Key}-settings-{tab.Header}");
                    }
                }
            }
        }
        session.SetTheme(ThemeVariant.Dark);
    }
}
