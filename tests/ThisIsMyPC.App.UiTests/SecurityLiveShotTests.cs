using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

[Trait("Category", "Diagnostic")]
public sealed class SecurityLiveShotTests
{
    [AvaloniaFact(Timeout = 120_000)]
    public async Task Security_OpensFromTheSidebar_ReadOnly()
    {
        using var session = UiSession.ForMainWindow("security-live");
        var main = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => main.SidebarGroups.Count > 0, 30_000, "sidebar");
        session.OpenModule("Security");
        await session.WaitForAsync(() => main.CurrentContent is SecurityViewModel, 30_000, "Security page");
        var security = (SecurityViewModel)main.CurrentContent!;
        for (var index = 0; index < security.Sections.Count; index++)
        {
            session.ClickText(security.Sections[index].Header);
            session.Pump();
            session.Screenshot($"section-{index}");
        }
    }
}
