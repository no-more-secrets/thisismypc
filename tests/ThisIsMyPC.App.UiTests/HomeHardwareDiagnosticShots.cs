using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

[Trait("Category", "Diagnostic")]
public sealed class HomeHardwareDiagnosticShots
{
    [AvaloniaFact]
    public async Task LiveHomeShowsHardwareWithinNormalPageEdges()
    {
        using var session = UiSession.ForMainWindow("home-hardware-live");
        var main = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => main.CurrentContent is HomeViewModel { IsHardwareLoading: false }, what: "hardware inventory");
        if (main.CurrentContent is HomeViewModel { FirstLaunchBanner.IsVisible: true }) session.ClickText("Dismiss");
        session.Screenshot("dark");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("light");
    }
}
