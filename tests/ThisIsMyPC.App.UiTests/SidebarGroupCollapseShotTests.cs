using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

[Trait("Category", "Diagnostic")]
public sealed class SidebarGroupCollapseShotTests
{
    [AvaloniaFact(Timeout = 120_000)]
    public async Task Group_headers_toggle_independently_in_both_sidebar_widths_and_themes()
    {
        using var session = UiSession.ForMainWindow("sidebar-group-collapse");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count >= 2, what: "sidebar groups");
        Assert.All(vm.SidebarGroups, group => Assert.True(group.IsExpanded));
        var first = vm.SidebarGroups[0];
        var second = vm.SidebarGroups[1];
        var active = first.Items.First(item => item.IsAvailable);
        session.Click(session.Find<Button>(button => ReferenceEquals(button.DataContext, active)));
        await session.WaitForAsync(() => !vm.IsModuleLoading && vm.CurrentContent is not null, what: "active module");
        var content = vm.CurrentContent;

        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            vm.IsSidebarCollapsed = false;
            first.IsExpanded = true;
            second.IsExpanded = true;
            session.Pump();
            session.Screenshot($"{theme.Key}-wide-open");
            session.Click(Header(first));
            Assert.False(first.IsExpanded);
            Assert.True(second.IsExpanded);
            Assert.Same(content, vm.CurrentContent);
            Assert.True(active.IsActive);
            Assert.Empty(session.FindAll<Button>(button => button.DataContext is SidebarItemViewModel item && first.Items.Contains(item)));
            Assert.Equal($"Expand {first.GroupName}", AutomationProperties.GetName(Header(first)));
            session.Screenshot($"{theme.Key}-wide-first-closed");

            vm.IsSidebarCollapsed = true;
            session.Pump();
            Assert.False(first.IsExpanded);
            Assert.True(second.IsExpanded);
            Assert.Equal(first.ToggleDescription, ToolTip.GetTip(Header(first)));
            Assert.Empty(session.FindAll<TextBlock>(text => text.Classes.Contains("sidebar-group-header")));
            session.Screenshot($"{theme.Key}-narrow-first-closed");

            // A native Button supplies Space/Enter handling and a discoverable focus stop.
            var header = Header(first);
            Assert.True(header.Focus());
            session.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            session.Window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            session.Pump();
            Assert.True(first.IsExpanded);
            Assert.True(second.IsExpanded);
            Assert.Equal($"Collapse {first.GroupName}", AutomationProperties.GetName(header));
            session.Click(Header(second));
            Assert.True(first.IsExpanded);
            Assert.False(second.IsExpanded);
            session.Screenshot($"{theme.Key}-narrow-second-closed");

            vm.IsSidebarCollapsed = false;
            session.Pump();
            Assert.True(first.IsExpanded);
            Assert.False(second.IsExpanded);
            Assert.Same(content, vm.CurrentContent);
            session.Click(Header(second));
            Assert.True(second.IsExpanded);
        }

        session.Window.Height = 620;
        foreach (var group in vm.SidebarGroups)
            group.IsExpanded = false;
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        foreach (var narrow in new[] { false, true })
        {
            session.SetTheme(theme);
            vm.IsSidebarCollapsed = narrow;
            session.Pump();
            Assert.Equal(vm.SidebarGroups.Count,
                session.FindAll<Button>(button => button.Classes.Contains("sidebar-group-toggle")).Count());
            session.Screenshot($"{theme.Key}-{(narrow ? "narrow" : "wide")}-short-all-closed");
        }

        Button Header(SidebarGroupViewModel group) => session.Find<Button>(button =>
            button.Classes.Contains("sidebar-group-toggle") && ReferenceEquals(button.DataContext, group));
    }
}
