using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

[Trait("Category", "Diagnostic")]
public class HardwareAlphaShotTests
{
    [AvaloniaFact(Timeout = 180_000)]
    public async Task HardwarePagesShowAlphaAndExplorerDoesNot()
    {
        using var session = UiSession.ForMainWindow("hardware-alpha");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0, timeoutMs: 30_000);
        foreach (var name in new[] { "System Control", "Cooling", "Monitoring" })
        {
            session.OpenModule(name);
            await session.WaitForAsync(() => vm.ContentTitle == name && vm.CurrentContent is not null,
                timeoutMs: 60_000, what: name);
            Assert.True(vm.IsCurrentFeatureAlpha);
            Assert.True(session.IsTextVisible("Alpha"));
            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                session.SetTheme(theme);
                session.Screenshot(name.Replace(' ', '-') + "-" + theme.Key);
            }
        }
        session.OpenModule("Explorer");
        await session.WaitForAsync(() => vm.CurrentContent is ShellViewModel, timeoutMs: 30_000);
        Assert.False(vm.IsCurrentFeatureAlpha);
        Assert.False(session.IsTextVisible("Alpha"));
        var search = ((ShellViewModel)vm.CurrentContent!).TaskbarChoiceSettings.Single(row => row.Label == "Taskbar search");
        Assert.DoesNotContain("restart", search.Description, StringComparison.OrdinalIgnoreCase);
    }
}
