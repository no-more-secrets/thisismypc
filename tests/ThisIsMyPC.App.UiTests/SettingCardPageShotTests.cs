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
                    // The two stacked boxes are the toolbar's right edge; they end where the rows' 16px lane starts.
                    var toggles = session.Find<StackPanel>(p => p.Name == "DisplayToggles");
                    Assert.Equal(23, card.Bounds.Width - toggles.TranslatePoint(default, card)!.Value.X - toggles.Bounds.Width, 0.5);
                    Assert.Equal(["Technical details", "Compact"],
                        toggles.Children.OfType<CheckBox>().Select(c => c.Content as string).ToArray());
                    // The stack and the search box share one height and one top: no height mismatch.
                    var search = session.Find<TextBox>(b => b.Watermark == "Search settings");
                    Assert.Equal(session.TopOf(search), session.TopOf(toggles), 0.5);
                    Assert.Equal(search.Bounds.Height, toggles.Bounds.Height, 0.5);
                    session.Screenshot($"{name}-{theme.Key}-{width}");
                }
            }
            // Every card starts full size with its registry path hidden, and the description
            // in the (i) tooltip like other modules, not as a paragraph on the card.
            Assert.All(AllCards(model), c => Assert.False(c.IsCompact));
            Assert.All(AllCards(model), c => Assert.False(c.IsRegistryDataVisible));
            Assert.Empty(session.FindAll<TextBlock>(t => t.Text is { } text && text.StartsWith("HK", StringComparison.Ordinal)));
            Assert.False(session.IsTextVisible(model.CardGroups[0].Cards[0].Description));
            Assert.False(session.IsTextVisible("Show technical details"));
            Assert.Equal(model.CardGroups[0].Cards[0].Description,
                session.Find<ToggleCard>(c => c.Title == model.CardGroups[0].Cards[0].DisplayName).Description);

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
    public void TechnicalDetailsBox_OpensEveryCard_AndCompactFoldsBadgesIntoTheTooltip()
    {
        using var vm = Privacy(new PendingChangesService());
        using var session = UiSession.ForView(Card(new SettingCardPageView()), vm, "card-pages", width: 1200, height: 800);
        var first = vm.CardGroups[0].Cards[0];
        var enforced = AllCards(vm).First(c => c.HasEnforcementBadge && c.HasReversionRisks);
        Assert.False(session.IsTextVisible("Show technical details"));
        Assert.False(session.IsTextVisible(first.WrappableSystemPath));
        Assert.True(session.IsTextVisible(enforced.EnforcementSummary!));
        Assert.True(session.IsTextVisible(enforced.ReversionRisksText!));

        var details = session.Find<CheckBox>(c => c.Content as string == "Technical details");
        session.Click(details);
        Assert.True(vm.ShowRegistryData);
        Assert.All(AllCards(vm), c => Assert.True(c.IsRegistryDataVisible));
        Assert.True(session.IsTextVisible(first.WrappableSystemPath));
        Assert.True(session.IsTextVisible(first.TechnicalStateText));
        session.Screenshot("privacy-page-details");

        session.Click(details);
        Assert.False(vm.ShowRegistryData);
        Assert.All(AllCards(vm), c => Assert.False(c.IsRegistryDataVisible));
        Assert.False(session.IsTextVisible(first.WrappableSystemPath));

        var compact = session.Find<CheckBox>(c => c.Content as string == "Compact");
        var fullHeight = session.Find<ToggleCard>(c => c.Title == enforced.DisplayName).Bounds.Height;
        session.Click(compact);
        Assert.True(vm.IsCompact);
        Assert.All(AllCards(vm), c => Assert.True(c.IsCompact));
        // The informational lines leave the card and ride along in the tooltip; the card gets shorter.
        Assert.False(session.IsTextVisible(enforced.EnforcementSummary!));
        Assert.False(session.IsTextVisible(enforced.ReversionRisksText!));
        var compactCard = session.Find<ToggleCard>(c => c.Title == enforced.DisplayName);
        Assert.True(compactCard.Bounds.Height < fullHeight, $"compact {compactCard.Bounds.Height} should be shorter than {fullHeight}");
        Assert.Contains(enforced.EnforcementSummary!, compactCard.Description, StringComparison.Ordinal);
        Assert.Contains(enforced.ReversionRisksText!, compactCard.Description, StringComparison.Ordinal);
        Assert.StartsWith(enforced.Description, compactCard.Description, StringComparison.Ordinal);
        session.Screenshot("privacy-compact");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("privacy-compact-light");
        session.SetTheme(ThemeVariant.Dark);

        // Both boxes together: the densest view still carries the technical lines.
        session.Click(details);
        Assert.True(session.IsTextVisible(first.WrappableSystemPath));
        session.Screenshot("privacy-compact-details");
    }
}
