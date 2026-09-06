using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ThisIsMyPC.App.Controls;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Settings;
using ThisIsMyPC.Modules.Power.Models;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Software.Models;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.App.UiTests;

public class ModuleEdgeTabShotTests
{
    [AvaloniaFact]
    public async Task EveryTabbedPage_FillsTheCardEdge_AtWideAndWrappedWidths()
    {
        var pending = new PendingChangesService();
        using var power = new PowerViewModel(new PowerScanData([
            new PowerPlan { PlanGuid = Guid.NewGuid(), Name = "Balanced", Description = "Balanced plan", IsActive = true },
        ], HibernateEnabled: true), pending, powerService: new UiFakePowerService([
            new PowerSettingInfo(Guid.NewGuid(), "Sleep", Guid.NewGuid(), "Sleep after", null,
                600, 300, "Seconds", true, 0, 3600, 1, []),
        ]));
        using var startup = new StartupViewModel(new StartupScanData([], []), pending);
        using var context = new ContextMenuViewModel([], pending, new UiFakeRegistryService());
        using var shell = new ShellViewModel(new ShellScanData([], new TaskbarSettings(1, true, false, false)), pending, new UiFakeRegistryService());
        using var software = new SoftwareViewModel(new SoftwareScanData([], new HashSet<string>(), true, "test", [], new HashSet<string>(), true), new PendingActionsService());
        var cardRegistry = new UiFakeRegistryService();
        using var privacy = new PrivacyViewModel(
            new ThisIsMyPC.Modules.Privacy.Services.PrivacySettingsReader(cardRegistry).ReadAll(), pending, cardRegistry);
        using var windowsUpdate = new WindowsUpdateViewModel(
            new ThisIsMyPC.Modules.WindowsUpdate.Services.WindowsUpdateSettingsReader(cardRegistry).ReadAll(), pending, cardRegistry);
        var pages = new (string Name, Control View, object Model)[]
        {
            ("privacy", new SettingCardPageView(), privacy),
            ("windows-update", new SettingCardPageView(), windowsUpdate),
            ("explorer", new ShellView(), shell),
            ("environment", new EnvironmentView(), new EnvironmentViewModel(new EnvironmentScanData([], []), pending)),
            ("settings", new SettingsView(), new SettingsViewModel(new SettingsService(Path.Combine(Path.GetTempPath(), $"tipc-tabs-{Guid.NewGuid():N}.json")), [])),
            ("software", new SoftwareView(), software),
            ("context-menu", new ContextMenuView(), context),
            ("startup", new StartupView(), startup),
            ("power", new PowerView(), power),
        };
        foreach (var (name, view, model) in pages)
        {
            var card = new Border
            {
                Margin = new Thickness(16), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Child = view,
            };
            card.Bind(Border.BackgroundProperty, card.GetResourceObservable("RaisedBrush"));
            card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("OutlineBrush"));
            using var session = UiSession.ForView(card, model, "module-edge-tabs", width: 1200, height: 800);
            if (name == "power")
            {
                ((ISearchNavigationTarget)power).NavigateToSearchResult("plan-settings", "Power plan settings");
                await session.WaitForAsync(() => !power.IsLoadingSettings, what: "power settings");
                Assert.True(power.IsSettingsView);
            }
            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                session.SetTheme(theme);
                foreach (var width in new[] { 1200, 800 })
                {
                    session.Window.Width = width;
                    session.Pump();
                    var strip = session.Find<Border>(b => b.Name == "PART_Strip" && b.IsEffectivelyVisible);
                    Assert.Equal(card.Bounds.Width - 2, strip.Bounds.Width, 0.5);
                    Assert.Equal(1, strip.TranslatePoint(default, card)!.Value.Y, 0.5);
                    var tabs = session.Find<TabControl>(t => t.IsEffectivelyVisible);
                    if (tabs is ModuleTabControl { Toolbar: Control toolbar })
                    {
                        Assert.True(session.TopOf(toolbar) >= session.TopOf(strip) + strip.Bounds.Height + 16);
                        Assert.Equal(25, toolbar.TranslatePoint(default, card)!.Value.X, 0.5);
                    }
                    session.Screenshot($"{name}-{theme.Key}-{width}");
                }
            }
            if (name == "startup")
            {
                var tabs = session.Find<ModuleTabControl>(_ => true);
                var selected = tabs.SelectedIndex;
                var search = session.Find<TextBox>(b => b.Classes.Contains("filter"));
                session.Type(search, "unmatched");
                Assert.Null(session.TryFind<Border>(b => b.Name == "PART_Strip"));
                Assert.True(search.IsEffectivelyVisible);
                session.Screenshot("startup-search");
                startup.AutorunFilterText = string.Empty;
                session.Pump();
                Assert.True(session.Find<Border>(b => b.Name == "PART_Strip").IsEffectivelyVisible);
                Assert.Equal(selected, tabs.SelectedIndex);
            }
        }
    }
}
