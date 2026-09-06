using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// A card-rendered module page: one tab per provider group, in provider order,
/// with the page's search box and display toggles above the tabs. Typing in the
/// search box replaces the tabs with one grouped list across every category;
/// clearing it brings the tabs back on the tab that was open. A global search
/// result selects the tab that owns the card and fills the search box with its
/// name, so the card is what the page shows.
/// </summary>
public abstract partial class SettingCardPageViewModel
    : ViewModelBase, IDisposable, ISearchFocusTarget, ISearchNavigationTarget, ITabbedPage
{
    public ObservableCollection<SettingCardGroupViewModel> CardGroups { get; } = [];

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    private string _searchText = string.Empty;

    /// <summary>True while the search box has text; the view swaps the tabs for the results list.</summary>
    public bool IsSearching => SearchText.Trim().Length > 0;

    [ObservableProperty]
    private string _searchSummary = string.Empty;

    [ObservableProperty]
    private bool _hasSearchResults = true;

    private readonly string _tabKey;
    private readonly DisplayModePreferencesStore? _displayModeStore;
    private bool _suppressModePersist;

    /// <summary>Page-wide technical details: sets the details panel open on every card. Persisted per page.</summary>
    [ObservableProperty]
    private bool _showRegistryData;

    /// <summary>Compact display mode: cards collapse to one line; the informational badge lines move into the (i) tooltip.</summary>
    [ObservableProperty]
    private bool _isCompact;

    protected SettingCardPageViewModel(
        string tabKey,
        IEnumerable<SettingCardSource> sources,
        IReadOnlyDictionary<string, string> sectionSubtitles,
        IPendingChangesService pendingChangesService,
        DisplayModePreferencesStore? displayModeStore,
        ICapabilityDetector? capabilityDetector,
        Services.IOwnerModeLifecycle? ownerMode)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(sectionSubtitles);
        _tabKey = tabKey;
        _displayModeStore = displayModeStore;

        var cards = sources
            .Select(source => new SettingCardViewModel(source, pendingChangesService, capabilityDetector, ownerMode))
            .ToList();

        // Group by GroupId in first-appearance order (provider order is authoritative).
        foreach (var group in cards.GroupBy(c => c.Model.GroupId ?? string.Empty))
        {
            CardGroups.Add(new SettingCardGroupViewModel
            {
                Header = group.Key,
                Subtitle = sectionSubtitles.TryGetValue(group.Key, out var subtitle) ? subtitle : null,
                Cards = group.ToList(),
            });
        }

        // Restore the page's persisted display mode; card flags follow.
        if (_displayModeStore?.Get(_tabKey) is { } mode)
        {
            _suppressModePersist = true;
            ShowRegistryData = mode.RegistryData;
            IsCompact = mode.Compact;
            _suppressModePersist = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        foreach (var group in CardGroups)
            group.ApplySearch(value.Trim());

        var matches = CardGroups.Sum(g => g.Cards.Count(c => c.IsSearchVisible));
        HasSearchResults = matches > 0;
        // The empty state has its own line; the count only reads when there is something to count.
        SearchSummary = !IsSearching || matches == 0 ? string.Empty
            : matches == 1 ? "1 setting matches"
            : $"{matches} settings match";
    }

    public void NavigateToSearchResult(string settingId, string displayName)
    {
        var index = IndexOfGroupOwning(settingId, displayName);
        if (index >= 0)
            SelectedTabIndex = index;
        SearchText = displayName;
    }

    private int IndexOfGroupOwning(string settingId, string displayName)
    {
        for (var i = 0; i < CardGroups.Count; i++)
        {
            if (CardGroups[i].Cards.Any(c => string.Equals(c.Model.SettingId, settingId, StringComparison.Ordinal)))
                return i;
        }

        for (var i = 0; i < CardGroups.Count; i++)
        {
            if (CardGroups[i].Cards.Any(c => string.Equals(c.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)))
                return i;
        }

        return -1;
    }

    // Each box touches only its own card flag, and mutates the existing card
    // VMs in place: the list is never rebuilt, so scroll position and pending
    // tint survive the other box.

    /// <summary>The page box sets every card's details panel.</summary>
    partial void OnShowRegistryDataChanged(bool value)
    {
        foreach (var card in AllCards())
            card.IsRegistryDataVisible = value;
        PersistDisplayMode();
    }

    partial void OnIsCompactChanged(bool value)
    {
        foreach (var card in AllCards())
            card.IsCompact = value;
        PersistDisplayMode();
    }

    private IEnumerable<SettingCardViewModel> AllCards() => CardGroups.SelectMany(g => g.Cards);

    private void PersistDisplayMode()
    {
        if (!_suppressModePersist)
            _displayModeStore?.Set(_tabKey, ShowRegistryData, IsCompact);
    }

    public void Dispose()
    {
        foreach (var group in CardGroups)
        {
            foreach (var card in group.Cards)
                card.Dispose();
        }
    }
}
