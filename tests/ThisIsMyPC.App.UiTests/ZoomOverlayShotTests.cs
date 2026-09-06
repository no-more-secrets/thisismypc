using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The zoom pill after Ctrl+plus: it floats over the page, so the apply bar
/// keeps its exact bounds and the status line stays empty; it goes away on
/// its own, and a burst of zoom presses keeps it up until the last delay ends.
/// Boots the real MainWindow, so Category=Diagnostic.
/// </summary>
[Trait("Category", "Diagnostic")]
public class ZoomOverlayShotTests
{
    [AvaloniaFact(Timeout = 120_000)]
    public async Task ZoomPill_FloatsOverThePage_AndHidesAfterTheLastChange()
    {
        using var session = UiSession.ForMainWindow("zoom-overlay");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0, what: "sidebar");
        vm.ZoomOverlayLifetime = TimeSpan.Zero; // deterministic frames; the hide is replayed by hand below
        var bar = session.Find<Border>(b => b.Name == "ApplyBar");

        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        foreach (var width in new[] { 1280, 900 })
        {
            session.SetTheme(theme);
            session.Window.Width = width;
            session.Window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.Control);
            vm.HideZoomOverlay(vm.ZoomOverlayGeneration);
            session.Pump();
            var barBefore = (Height: bar.Bounds.Height, Status: vm.StatusMessage);
            Assert.False(vm.IsZoomOverlayVisible);

            session.Window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.Control);
            session.Pump();
            Assert.Equal(110, vm.ZoomPercent);
            Assert.True(vm.IsZoomOverlayVisible);
            Assert.True(session.IsTextVisible("Zoom 110%"));
            Assert.Equal(string.Empty, vm.StatusMessage);
            var pill = session.Find<Border>(b => b.Name == "ZoomOverlay");
            Assert.False(pill.IsHitTestVisible);
            // Same footer height (logical, so zoom does not change it): the pill took
            // no layout space, and it sits over the page, well above the footer.
            Assert.Equal(barBefore.Height, bar.Bounds.Height, 0.5);
            Assert.True(session.TopOf(pill) + pill.Bounds.Height * vm.UiScale < session.TopOf(bar) - 100);
            session.Screenshot($"pill-{theme.Key}-{width}");

            // A burst: the first press's timer must not hide the second press's pill.
            var first = vm.ZoomOverlayGeneration;
            session.Window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.Control);
            session.Pump();
            Assert.True(session.IsTextVisible("Zoom 120%"));
            vm.HideZoomOverlay(first);
            session.Pump();
            Assert.True(vm.IsZoomOverlayVisible);
            vm.HideZoomOverlay(vm.ZoomOverlayGeneration);
            session.Pump();
            Assert.False(vm.IsZoomOverlayVisible);
            Assert.False(session.IsTextVisible("Zoom 120%"));
        }

        session.Window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.Control);
        vm.HideZoomOverlay(vm.ZoomOverlayGeneration);
        session.SetTheme(ThemeVariant.Dark);
    }

    [AvaloniaFact(Timeout = 120_000)]
    public async Task ZoomPill_HidesOnItsOwn_WithTheRealTimer()
    {
        using var session = UiSession.ForMainWindow("zoom-overlay");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0, what: "sidebar");
        vm.ZoomOverlayLifetime = TimeSpan.FromMilliseconds(150);

        session.Window.KeyPressQwerty(PhysicalKey.Minus, RawInputModifiers.Control);
        Assert.True(vm.IsZoomOverlayVisible);
        await session.WaitForAsync(() => !vm.IsZoomOverlayVisible, timeoutMs: 5000, what: "pill auto-hide");

        session.Window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.Control);
        await session.WaitForAsync(() => !vm.IsZoomOverlayVisible, timeoutMs: 5000, what: "pill auto-hide after reset");
        Assert.Equal(100, vm.ZoomPercent);
    }
}
