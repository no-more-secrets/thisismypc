using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Interop.Win32.Packages;

namespace ThisIsMyPC.App.UiTests;

[Trait("Category", "Diagnostic")]
public sealed class SoftwareCatalogLiveShotTests
{
    [AvaloniaFact(Timeout = 180_000)]
    public async Task DefaultWindowShowsThreeColumnsWithLiveInstalledState()
    {
        using var session = UiSession.ForMainWindow("software-live", services =>
        {
            services.RemoveAll<IWingetService>();
            services.AddSingleton<IWingetService, WingetService>();
        });
        session.Window.Width = 1200;
        var main = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => main.SidebarGroups.Count > 0, what: "sidebar");
        session.OpenModule("Software");
        await session.WaitForAsync(() => main.CurrentContent is SoftwareViewModel,
            timeoutMs: 120_000, what: "Software scan");
        var vm = (SoftwareViewModel)main.CurrentContent!;
        Assert.True(vm.InstalledStateKnown);
        var cards = session.FindAll<Border>(b => b.Classes.Contains("card")
            && b.DataContext is SoftwareAppViewModel { Category: "Browsers" }).Take(4).ToArray();
        Assert.Equal(4, cards.Length);
        var positions = cards.Select(c => c.TranslatePoint(default, session.Window)!.Value).ToArray();
        Assert.Equal(positions[0].Y, positions[2].Y);
        Assert.True(positions[3].Y > positions[0].Y);
        session.Screenshot("default-catalog");
        // Capture the detected catalog entries for a read-only comparison with winget list.
        File.WriteAllLines(Path.Combine(session.ShotDirectory, "installed.txt"),
            vm.FilteredApps.Where(a => a.IsInstalled).Select(a => a.WingetId));
    }
}
