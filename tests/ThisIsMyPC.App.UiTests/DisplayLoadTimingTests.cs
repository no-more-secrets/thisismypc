using System.Diagnostics;
using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// Times the Display page as a person meets it: click Display, wait for the
/// content, leave, come back, three times on the real service graph. Writes
/// artifacts/diagnostics/display-timing/page-load.txt. Diagnostic: real DDC.
/// </summary>
[Trait("Category", "Diagnostic")]
public class DisplayLoadTimingTests
{
    [AvaloniaFact(Timeout = 300_000)]
    public async Task TimeDisplayPageLoads()
    {
        using var session = UiSession.ForMainWindow("display-timing");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");

        var lines = new List<string>();
        for (var pass = 0; pass < 3; pass++)
        {
            var sw = Stopwatch.StartNew();
            session.ClickText("Display");
            await session.WaitForAsync(
                () => vm.CurrentContent is DisplayViewModel && !vm.IsModuleLoading,
                timeoutMs: 120_000, what: "Display content");
            sw.Stop();
            var display = (DisplayViewModel)vm.CurrentContent!;
            lines.Add($"pass {pass}: Display page ready in {sw.ElapsedMilliseconds} ms, monitors={display.Monitors.Count}, refreshing={display.IsRefreshing}, pending={display.Monitors.Count(m => m.FeaturesPending)}");
            if (pass == 0)
            {
                session.Screenshot("quick-open");
                var fill = Stopwatch.StartNew();
                await session.WaitForAsync(() => !display.IsRefreshing, timeoutMs: 120_000, what: "background full scan");
                lines.Add($"pass 0: full scan filled in after {fill.ElapsedMilliseconds} ms, pending={display.Monitors.Count(m => m.FeaturesPending)}, vendor rows={display.Monitors.Sum(m => m.VendorFeatures.Count + m.AdvancedVendorFeatures.Count)}");
                session.Screenshot("after-fill-in");
            }

            session.ClickText("Home");
            await session.WaitForAsync(() => vm.CurrentContent is HomeViewModel, what: "Home");
        }

        var dir = Path.Combine(session.ShotDirectory, "..", "..", "diagnostics", "display-timing");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "page-load.txt"), lines);
    }
}
