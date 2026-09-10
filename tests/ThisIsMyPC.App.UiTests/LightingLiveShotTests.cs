using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// Diagnostic: the real Lighting page on the real service graph. Opening the
/// page runs the built-in controllers' detection (HID enumeration, GPU I2C)
/// and lists this PC's devices. Nothing is written to a device.
/// </summary>
[Trait("Category", "Diagnostic")]
public class LightingLiveShotTests
{
    [AvaloniaFact(Timeout = 180_000)]
    public async Task LightingPage_ListsThisPcsDevices()
    {
        using var session = UiSession.ForMainWindow("lighting-live");
        var viewModel = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => viewModel.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");

        session.ClickText("Lighting");
        await session.WaitForAsync(
            () => viewModel.CurrentContent is HardwareTabViewModel && viewModel.ContentTitle == "Lighting",
            timeoutMs: 60_000, what: "Lighting content load");
        session.Screenshot("opening");

        await session.WaitForAsync(
            () => viewModel.CurrentContent is HardwareTabViewModel { Lighting.HasDevices: true, IsRefreshing: false },
            timeoutMs: 90_000, what: "detection and device list");
        session.Pump();
        session.Screenshot("devices-dark");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("devices-light");

        var tab = (HardwareTabViewModel)viewModel.CurrentContent!;
        Assert.True(tab.IsAvailable, tab.Explanation);
        Assert.True(tab.Decision.LiveWritesAllowed);
        Assert.NotEmpty(tab.Lighting!.Devices);
    }
}
