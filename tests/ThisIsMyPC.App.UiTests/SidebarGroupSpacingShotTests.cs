using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

public class SidebarGroupSpacingShotTests
{
    [AvaloniaFact(Timeout = 60_000)]
    [Trait("Category", "Diagnostic")]
    public async Task CollapsedSidebarKeepsGroupSpacingWithoutTitles()
    {
        using var session = UiSession.ForMainWindow("sidebar-group-spacing");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0, what: "sidebar");
        session.Window.Width = 1078;
        session.Window.Height = 816;
        vm.ChangeZoom(-1);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            vm.IsSidebarCollapsed = false;
            session.Pump();
            var expanded = session.FindAll<Button>(b => b.DataContext is SidebarItemViewModel)
                .ToDictionary(b => ((SidebarItemViewModel)b.DataContext!).Name, b => (Top: session.TopOf(b), Bottom: session.TopOf(b) + b.Bounds.Height * vm.UiScale));
            session.Screenshot($"{theme.Key}-expanded");
            vm.IsSidebarCollapsed = true;
            session.Pump();
            var collapsed = session.FindAll<Button>(b => b.DataContext is SidebarItemViewModel)
                .ToDictionary(b => ((SidebarItemViewModel)b.DataContext!).Name, b => (Top: session.TopOf(b), Bottom: session.TopOf(b) + b.Bounds.Height * vm.UiScale));
            for (var i = 1; i < vm.SidebarGroups.Count; i++)
            {
                var last = vm.SidebarGroups[i - 1].Items.Last().Name;
                var first = vm.SidebarGroups[i].Items.First().Name;
                Assert.Equal(expanded[first].Top - expanded[last].Bottom,
                    collapsed[first].Top - collapsed[last].Bottom, 0.01);
            }
            Assert.Empty(session.FindAll<TextBlock>(t => t.Classes.Contains("sidebar-group-header")));
            Assert.Equal(vm.SidebarGroups.Count,
                session.FindAll<Button>(b => b.Classes.Contains("sidebar-group-toggle")).Count());
            session.Screenshot($"{theme.Key}-collapsed");
        }
    }
}
