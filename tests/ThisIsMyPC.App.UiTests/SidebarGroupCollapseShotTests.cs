using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The sidebar is an accordion: the group holding the open module is open and
/// every other group folds; a folded header opens that group's first module.
/// Nobody folds or opens a group by hand.
/// </summary>
[Trait("Category", "Diagnostic")]
public sealed class SidebarGroupCollapseShotTests
{
    [AvaloniaFact(Timeout = 120_000)]
    public async Task Groups_follow_the_open_module_in_both_sidebar_widths_and_themes()
    {
        using var session = UiSession.ForMainWindow("sidebar-group-collapse");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count >= 2, what: "sidebar groups");
        var first = vm.SidebarGroups[0];
        var second = vm.SidebarGroups[1];

        // Home: the first group is open, the rest folded, so the sidebar never shows only headers.
        Assert.True(first.IsExpanded);
        Assert.All(vm.SidebarGroups.Skip(1), group => Assert.False(group.IsExpanded));

        var active = second.Items.First(item => item.IsAvailable);
        session.Click(Header(second));
        await session.WaitForAsync(() => !vm.IsModuleLoading && vm.CurrentContent is not null, what: "active module");
        Assert.True(active.IsActive);
        Assert.True(second.IsExpanded);
        Assert.False(first.IsExpanded);
        Assert.Empty(session.FindAll<Button>(button => button.DataContext is SidebarItemViewModel item && first.Items.Contains(item)));
        Assert.Equal($"Open {first.GroupName}", AutomationProperties.GetName(Header(first)));
        Assert.Equal(second.GroupName, AutomationProperties.GetName(Header(second)));
        var content = vm.CurrentContent;

        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            vm.IsSidebarCollapsed = false;
            session.Pump();
            session.Screenshot($"{theme.Key}-wide-second-open");

            // The open group's header is inert: same page, same group.
            session.Click(Header(second));
            session.Pump();
            Assert.Same(content, vm.CurrentContent);
            Assert.True(second.IsExpanded);
            Assert.False(first.IsExpanded);

            vm.IsSidebarCollapsed = true;
            session.Pump();
            Assert.True(second.IsExpanded);
            Assert.Equal(first.HeaderDescription, ToolTip.GetTip(Header(first)));
            Assert.Empty(session.FindAll<TextBlock>(text => text.Classes.Contains("sidebar-group-header")));
            session.Screenshot($"{theme.Key}-narrow-second-open");

            // A native Button supplies Space/Enter handling and a discoverable focus stop:
            // the folded header opens its first module, which opens the group.
            var header = Header(first);
            Assert.True(header.Focus());
            session.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            session.Window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            await session.WaitForAsync(() => !vm.IsModuleLoading && first.Items.Any(i => i.IsActive), what: "first group module");
            Assert.True(first.IsExpanded);
            Assert.False(second.IsExpanded);
            Assert.Equal(first.GroupName, AutomationProperties.GetName(Header(first)));
            session.Screenshot($"{theme.Key}-narrow-first-open");

            vm.IsSidebarCollapsed = false;
            session.Pump();
            Assert.True(first.IsExpanded);
            Assert.False(second.IsExpanded);
            session.Screenshot($"{theme.Key}-wide-first-open");

            // Back to the second group for the next theme pass.
            session.Click(Header(second));
            await session.WaitForAsync(() => !vm.IsModuleLoading && second.Items.Any(i => i.IsActive), what: "second group module");
            content = vm.CurrentContent;
        }

        // Home keeps the last open group rather than folding everything.
        vm.OpenHomeCommand.Execute(null);
        session.Pump();
        Assert.True(second.IsExpanded);
        Assert.False(first.IsExpanded);
        Assert.Equal(vm.SidebarGroups.Count,
            session.FindAll<Button>(button => button.Classes.Contains("sidebar-group-toggle")).Count());

        Button Header(SidebarGroupViewModel group) => session.Find<Button>(button =>
            button.Classes.Contains("sidebar-group-toggle") && ReferenceEquals(button.DataContext, group));
    }
}
