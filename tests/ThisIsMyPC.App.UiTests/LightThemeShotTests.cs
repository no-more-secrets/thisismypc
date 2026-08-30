using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Software.Models;
using ThisIsMyPC.Modules.Software.Services;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// CI-safe: renders representative views in the Light variant and switches
/// back and forth to prove DynamicResource restyling works live. Every session
/// resets to Dark so other test classes are unaffected by run order.
/// </summary>
public class LightThemeShotTests
{
    private static SoftwareScanData CreateScanData() => new(
        Catalog: SoftwareCatalog.Entries,
        InstalledWingetIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Git.Git", "Mozilla.Firefox" },
        InstalledStateKnown: true,
        WingetVersion: "v1.9.0-uitest",
        WindowsApps: WindowsAppsCatalog.Entries,
        PresentAppxPackageIds: WindowsAppsCatalog.Entries.Take(20).Select(e => e.PackageId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase),
        AppxStateKnown: true,
        Upgradable: [new("Mozilla.Firefox", "Mozilla Firefox", "133.0", "134.0.1")],
        UpgradableStateKnown: true);

    [AvaloniaFact]
    public void SoftwareView_RendersInLightAndSwitchesBack()
    {
        var queue = new PendingActionsService();
        var viewModel = new SoftwareViewModel(CreateScanData(), queue);
        using var session = UiSession.ForView(new SoftwareView(), viewModel, "light-theme");

        try
        {
            session.SetTheme(ThemeVariant.Light);
            session.Screenshot("software-light");

            Assert.True(session.IsTextVisible("App Catalog"));

            // Live switch back must restyle without rebuilding the view.
            session.SetTheme(ThemeVariant.Dark);
            session.Screenshot("software-dark-after-switch");
            Assert.True(session.IsTextVisible("App Catalog"));
        }
        finally
        {
            session.SetTheme(ThemeVariant.Dark);
        }
    }
}
