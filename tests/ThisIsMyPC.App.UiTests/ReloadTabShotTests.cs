using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

public class ReloadTabShotTests
{
    [AvaloniaFact(Timeout = 300_000)]
    [Trait("Category", "Diagnostic")]
    public async Task ReloadButtonAndCtrlR_KeepEachPagesSelectedTab()
    {
        using var session = UiSession.ForMainWindow("reload-tabs");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0, what: "sidebar");
        foreach (var name in new[] { "Explorer", "Environment", "Context Menus", "Startup & Services", "Software", "Settings", "Power Plans", "Windows Annoyances", "Windows Update", "Privacy & Telemetry" })
        {
            session.ClickText(name);
            await session.WaitForAsync(() => vm.CurrentContent is ITabbedPage && vm.ContentTitle == name && !vm.IsModuleLoading,
                timeoutMs: 120_000, what: name);
            if (vm.CurrentContent is PowerViewModel power)
            {
                await power.OpenSettingsCommand.ExecuteAsync(power.Plans.First(p => p.IsActive));
                session.Pump();
            }
            var tabs = session.Find<TabControl>(_ => true);
            var targetTab = session.FindAll<TabItem>(_ => true).Last();
            var selected = tabs.IndexFromContainer(targetTab);
            session.Click(targetTab);
            Assert.Equal(selected, ((ITabbedPage)vm.CurrentContent!).SelectedTabIndex);
            foreach (var keyboard in new[] { false, true })
            {
                var previous = vm.CurrentContent;
                if (keyboard)
                {
                    session.Find<TextBox>(b => b.Watermark == "Search settings...").Focus();
                    session.Window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.Control);
                }
                else
                    session.Click(session.Find<Button>(b => AutomationProperties.GetName(b) == "Refresh this page"));
                await session.WaitForAsync(() => vm.CurrentContent is ITabbedPage && !ReferenceEquals(previous, vm.CurrentContent) && !vm.IsModuleLoading
                    && (vm.CurrentContent is not PowerViewModel refreshedPower || (refreshedPower.IsSettingsView && !refreshedPower.IsLoadingSettings)),
                    timeoutMs: 120_000, what: $"reload {name}");
                Assert.Equal(selected, session.Find<TabControl>(_ => true).SelectedIndex);
                session.Screenshot($"{name}-{keyboard}");
            }
        }
    }
}
