using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ThisIsMyPC.App.Icons;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

public class FluentIconShotTests
{
    [AvaloniaFact]
    public void CatalogRendersAtCommonSizesAndInheritsForeground()
    {
        var grid = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var symbol in Enum.GetValues<FluentSymbol>())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            foreach (var size in new[] { 14, 18, 20, 30 })
                row.Children.Add(new FluentIcon { Symbol = symbol, Width = size, Height = size });
            row.Children.Add(new FluentIcon { Symbol = symbol, IsFilled = true, Width = 20, Height = 20 });
            var cell = new StackPanel { Width = 250, Height = 64, Margin = new Thickness(8) };
            cell.Children.Add(new TextBlock { Text = symbol.ToString(), FontSize = 12 });
            cell.Children.Add(row);
            grid.Children.Add(cell);
        }
        using var session = UiSession.ForView(grid, new object(), "fluent-icons", width: 1080, height: 900);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot($"catalog-{theme.Key}");
            foreach (var icon in session.FindAll<FluentIcon>(_ => true))
            {
                Assert.NotNull(icon.Foreground);
                Assert.True(icon.Bounds.Width > 0);
            }
        }
    }

    [AvaloniaFact(Timeout = 60_000)]
    [Trait("Category", "Diagnostic")]
    public async Task SelectedNavigationUsesFilledIconsAndChangesWithSelection()
    {
        using var session = UiSession.ForMainWindow("fluent-navigation");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0, what: "sidebar");
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.ClickText("Home");
            session.Pump();
            Assert.True(session.Find<FluentIcon>(i => i.Symbol == FluentSymbol.Home).IsFilled);
            var moduleButton = session.FindAll<Button>(b => b.DataContext is SidebarItemViewModel { IsAvailable: true }).First();
            session.Click(moduleButton);
            session.Pump();
            Assert.False(session.Find<FluentIcon>(i => i.Symbol == FluentSymbol.Home).IsFilled);
            foreach (var icon in session.FindAll<FluentIcon>(i => i.DataContext is SidebarItemViewModel))
                Assert.Equal(((SidebarItemViewModel)icon.DataContext!).IsActive, icon.IsFilled);
            session.Screenshot($"module-{theme.Key}");
            vm.IsSidebarCollapsed = true;
            session.Pump();
            session.Screenshot($"collapsed-{theme.Key}");
            vm.IsSidebarCollapsed = false;
        }
    }
}

