using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.Controls;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances.Models;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Privacy.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Services;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The shared card page (Annoyances, Privacy, Windows Update): one card-edge
/// tab per section, search that replaces the tabs, global search landing on
/// the owning tab, and technical details closed until asked for. Fake
/// registry, so CI-safe.
/// </summary>
public class SettingCardPageShotTests
{
    private static AnnoyancesViewModel Annoyances(IPendingChangesService pending)
    {
        var registry = new UiFakeRegistryService();
        var reader = new AnnoyancesSettingsReader(registry);
        var scan = new AnnoyancesScanData(
            reader.ReadAll(), reader.ReadBingSearch(), reader.ReadSettingsSuggestedContent(),
            reader.ReadCopilotPolicy(), reader.ReadRecall(), reader.ReadLockScreenAds(),
            reader.ReadPreinstalledApps(), reader.ReadEdgeDebloat(), reader.ReadActivityHistory());
        return new AnnoyancesViewModel(scan, pending, registry);
    }

    private static PrivacyViewModel Privacy(IPendingChangesService pending)
    {
        var registry = new UiFakeRegistryService();
        return new PrivacyViewModel(new PrivacySettingsReader(registry).ReadAll(), pending, registry);
    }

    private static WindowsUpdateViewModel WindowsUpdate(IPendingChangesService pending)
    {
        var registry = new UiFakeRegistryService();
        return new WindowsUpdateViewModel(new WindowsUpdateSettingsReader(registry).ReadAll(), pending, registry);
    }

    private static Border Card(Control view)
    {
        var card = new Border
        {
            Name = "TestCard", Margin = new Thickness(16), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Child = view,
        };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("RaisedBrush"));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("OutlineBrush"));
        return card;
    }

    private static IEnumerable<SettingCardViewModel> AllCards(SettingCardPageViewModel vm)
        => vm.CardGroups.SelectMany(g => g.Cards);

    [AvaloniaFact]
    public void EveryCardPage_HasOneTabPerSection_InBothThemesAndWidths()
    {
        var pending = new PendingChangesService();
        var pages = new (string Name, SettingCardPageViewModel Model)[]
        {
            ("annoyances", Annoyances(pending)),
            ("privacy", Privacy(pending)),
            ("windows-update", WindowsUpdate(pending)),
        };
        foreach (var (name, model) in pages)
        {
            using var _ = model;
            var card = Card(new SettingCardPageView());
            using var session = UiSession.ForView(card, model, "card-pages", width: 1200, height: 800);
            Assert.Equal(model.CardGroups.Count, session.FindAll<TabItem>(_ => true).Count());
            Assert.True(model.CardGroups.Count >= 3, $"{name} should have at least three sections");
            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                session.SetTheme(theme);
                foreach (var width in new[] { 1200, 800 })
                {
                    session.Window.Width = width;
                    session.Pump();
                    var strip = session.Find<Border>(b => b.Name == "PART_Strip");
                    Assert.Equal(card.Bounds.Width - 2, strip.Bounds.Width, 0.5);
                    Assert.Equal(session.TopOf(card) + 1, session.TopOf(strip), 0.5);
                    var tabs = session.Find<ModuleTabControl>(_ => true);
                    var toolbar = (Control)tabs.Toolbar!;
                    Assert.True(session.TopOf(toolbar) >= session.TopOf(strip) + strip.Bounds.Height + 16);
                    Assert.Equal(25, toolbar.TranslatePoint(default, card)!.Value.X, 0.5);
                    // The toggles are the toolbar's right edge; they end where the rows' 16px lane starts.
                    var compact = session.Find<ToggleButton>(b => b.Content as string == "Compact");
                    Assert.Equal(23, card.Bounds.Width - compact.TranslatePoint(default, card)!.Value.X - compact.Bounds.Width, 0.5);
                    session.Screenshot($"{name}-{theme.Key}-{width}");
                }
            }
            // Every card starts with its description on the card and its registry path hidden.
            Assert.All(AllCards(model), c => Assert.True(c.IsDescriptionVisible));
            Assert.All(AllCards(model), c => Assert.False(c.IsRegistryDataVisible));
            Assert.Empty(session.FindAll<TextBlock>(t => t.Text is { } text && text.StartsWith("HK", StringComparison.Ordinal)));
            Assert.True(session.IsTextVisible(model.CardGroups[0].Cards[0].Description));

            // The second tab opens on a click and shows its own cards.
            session.ClickText(model.CardGroups[1].Header);
            Assert.Equal(1, model.SelectedTabIndex);
            Assert.True(session.IsTextVisible(model.CardGroups[1].Cards[0].DisplayName));
            session.Screenshot($"{name}-second-tab");
        }
    }

    [AvaloniaFact]
    public void Search_ReplacesTheTabs_AndClearingRestoresTheOpenTab()
    {
        using var vm = Annoyances(new PendingChangesService());
        using var session = UiSession.ForView(Card(new SettingCardPageView()), vm, "card-pages", width: 1200, height: 800);
        session.ClickText("Advertising & Tracking");
        Assert.Equal(2, vm.SelectedTabIndex);

        var box = session.Find<TextBox>(b => b.Watermark == "Search settings");
        session.Type(box, "copilot");
        Assert.True(vm.IsSearching);
        Assert.Null(session.TryFind<Border>(b => b.Name == "PART_Strip"));
        Assert.True(session.IsTextVisible("Disable Windows Copilot"));
        Assert.True(session.IsTextVisible("AI Features"));
        Assert.False(session.IsTextVisible("Disable Bing web search in Start Menu"));
        Assert.True(session.IsTextVisible(vm.SearchSummary));
        Assert.True(vm.HasSearchResults);
        session.Screenshot("annoyances-search-copilot");

        vm.SearchText = "nothing matches this";
        session.Pump();
        Assert.False(vm.HasSearchResults);
        Assert.True(session.IsTextVisible("No settings match"));
        session.Screenshot("annoyances-search-empty");

        vm.SearchText = string.Empty;
        session.Pump();
        Assert.True(session.Find<Border>(b => b.Name == "PART_Strip").IsEffectivelyVisible);
        Assert.Equal(2, vm.SelectedTabIndex);
        Assert.True(session.IsTextVisible("Disable activity history"));
    }

    [AvaloniaFact]
    public void GlobalSearchResult_SelectsTheOwningTab_AndFocusesTheCard()
    {
        var pending = new PendingChangesService();
        var pages = new (SettingCardPageViewModel Model, string SettingId, string Name, string Tab)[]
        {
            (Annoyances(pending), "copilot", "Disable Windows Copilot", "AI Features"),
            (Privacy(pending), "inking-typing", "Disable inking and typing personalization", "Personalization"),
            (WindowsUpdate(pending), "restart-notifications", "Notify when a restart is required", "Update Experience"),
        };
        foreach (var (model, settingId, name, tab) in pages)
        {
            using var _ = model;
            using var session = UiSession.ForView(Card(new SettingCardPageView()), model, "card-pages", width: 1200, height: 800);
            var expected = model.CardGroups.ToList().FindIndex(g => g.Header == tab);
            Assert.True(expected >= 0, $"{tab} tab missing");
            ((ISearchNavigationTarget)model).NavigateToSearchResult(settingId, name);
            session.Pump();
            Assert.Equal(expected, model.SelectedTabIndex);
            Assert.Equal(name, model.SearchText);
            Assert.True(session.IsTextVisible(name));
            session.Screenshot($"global-search-{settingId}");

            // Clearing the search leaves the owning tab open with the card in view.
            model.SearchText = string.Empty;
            session.Pump();
            Assert.Equal(expected, session.Find<TabControl>(_ => true).SelectedIndex);
            Assert.True(session.IsTextVisible(name));
        }
    }

    [AvaloniaFact]
    public void TechnicalDetails_OpenPerCard_OrForTheWholePage_AndCompactMovesDescriptionsToTheTooltip()
    {
        using var vm = Privacy(new PendingChangesService());
        using var session = UiSession.ForView(Card(new SettingCardPageView()), vm, "card-pages", width: 1200, height: 800);
        var first = vm.CardGroups[0].Cards[0];
        Assert.True(session.IsTextVisible("Show technical details"));
        Assert.False(session.IsTextVisible(first.WrappableSystemPath));

        session.ClickText("Show technical details");
        Assert.True(first.IsRegistryDataVisible);
        Assert.True(session.IsTextVisible(first.WrappableSystemPath));
        Assert.True(session.IsTextVisible(first.TechnicalStateText));
        Assert.True(session.IsTextVisible("Hide technical details"));
        Assert.Single(AllCards(vm), c => c.IsRegistryDataVisible);
        session.Screenshot("privacy-one-card-details");

        session.ClickText("Technical details");
        Assert.True(vm.ShowRegistryData);
        Assert.All(AllCards(vm), c => Assert.True(c.IsRegistryDataVisible));
        session.Screenshot("privacy-page-details");

        session.ClickText("Hide technical details");
        Assert.False(first.IsRegistryDataVisible);
        Assert.True(vm.ShowRegistryData);

        session.ClickText("Technical details");
        Assert.All(AllCards(vm), c => Assert.False(c.IsRegistryDataVisible));

        Assert.All(AllCards(vm), c => Assert.Null(c.TooltipDescription));
        session.ClickText("Compact");
        Assert.True(vm.IsCompact);
        Assert.All(AllCards(vm), c => Assert.False(c.IsDescriptionVisible));
        Assert.All(AllCards(vm), c => Assert.Equal(c.Description, c.TooltipDescription));
        Assert.False(session.IsTextVisible(first.Description));
        session.Screenshot("privacy-compact");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("privacy-compact-light");
    }
}
