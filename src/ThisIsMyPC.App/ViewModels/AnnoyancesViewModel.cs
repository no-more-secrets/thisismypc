using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances.Models;
using ThisIsMyPC.Modules.Annoyances.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// First card-rendered module tab (Epic 10). The module's AnnoyancesCardProvider
/// supplies SettingCardSources; this VM wraps them in interactive card VMs grouped
/// by section, in provider order.
/// </summary>
public partial class AnnoyancesViewModel : ViewModelBase, IDisposable
{
    public ObservableCollection<SettingCardGroupViewModel> CardGroups { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        foreach (var group in CardGroups)
            group.ApplySearch(value);
    }

    // Section explainer lines carried over from the pre-card view.
    private static readonly IReadOnlyDictionary<string, string> SectionSubtitles =
        new Dictionary<string, string>
        {
            ["Nag Screens & Suggestions"] = "Suppress setup nags, welcome pages, tips, suggestions, and lock screen ads",
            ["Bing Search & Edge"] = "Keep Start Menu search local and stop Edge shortcuts from reappearing",
            ["Advertising & Tracking"] = "Quick toggles for the Advertising ID, activity history, and suggested content",
            ["Gaming & Accessibility"] = "Game DVR, GPU scheduling, and accidental accessibility shortcut prompts",
            ["AI Features"] = "Windows Copilot, Recall, and the Edge sidebar",
        };

    private const string TabKey = "annoyances";

    private readonly DisplayModePreferencesStore? _displayModeStore;
    private bool _suppressModePersist;

    /// <summary>Registry Data display mode (10-2): shows raw paths and values on every card.</summary>
    [ObservableProperty]
    private bool _showRegistryData;

    /// <summary>Compact display mode (10-2): collapses card descriptions to a dense toggle list.</summary>
    [ObservableProperty]
    private bool _isCompact;

    public AnnoyancesViewModel(
        AnnoyancesScanData scanData,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService,
        DisplayModePreferencesStore? displayModeStore = null,
        ICapabilityDetector? capabilityDetector = null)
    {
        _displayModeStore = displayModeStore;
        // Factories re-read live state at stage time — a scan-time snapshot would bake
        // stale BeforeValues into the descriptors after the first apply.
        var provider = new AnnoyancesCardProvider(new AnnoyancesSettingsReader(registryService));

        var cards = provider.BuildCards(scanData)
            .Select(source => new SettingCardViewModel(source, pendingChangesService, capabilityDetector))
            .ToList();

        // Group by GroupId in first-appearance order (provider order is authoritative).
        foreach (var group in cards.GroupBy(c => c.Model.GroupId ?? string.Empty))
        {
            CardGroups.Add(new SettingCardGroupViewModel
            {
                Header = group.Key,
                Subtitle = SectionSubtitles.TryGetValue(group.Key, out var subtitle) ? subtitle : null,
                Cards = group.ToList(),
            });
        }

        // Restore the tab's persisted display mode; card flags follow.
        if (_displayModeStore?.Get(TabKey) is { } mode)
        {
            _suppressModePersist = true;
            ShowRegistryData = mode.RegistryData;
            IsCompact = mode.Compact;
            _suppressModePersist = false;
        }
    }

    partial void OnShowRegistryDataChanged(bool value) => ApplyDisplayMode();

    partial void OnIsCompactChanged(bool value) => ApplyDisplayMode();

    /// <summary>
    /// Mode switches mutate the existing card VMs in place — the list is never
    /// rebuilt, so scroll position and pending tint survive by construction.
    /// </summary>
    private void ApplyDisplayMode()
    {
        foreach (var group in CardGroups)
        {
            foreach (var card in group.Cards)
            {
                card.IsDescriptionVisible = !IsCompact;
                card.IsRegistryDataVisible = ShowRegistryData;
            }
        }

        if (!_suppressModePersist)
            _displayModeStore?.Set(TabKey, ShowRegistryData, IsCompact);
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
