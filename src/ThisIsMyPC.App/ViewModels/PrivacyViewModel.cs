using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Privacy.Models;
using ThisIsMyPC.Modules.Privacy.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Card-rendered Privacy &amp; Telemetry tab. The module's PrivacyCardProvider
/// supplies SettingCardSources; this VM wraps them in interactive card VMs grouped
/// by section, in provider order (Annoyances pattern).
/// </summary>
public partial class PrivacyViewModel : ViewModelBase, IDisposable
{
    public ObservableCollection<SettingCardGroupViewModel> CardGroups { get; } = [];

    private static readonly IReadOnlyDictionary<string, string> SectionSubtitles =
        new Dictionary<string, string>
        {
            ["Diagnostic Data"] = "Diagnostic data collection and crash reporting",
            ["Permissions & Tracking"] = "Location and app launch tracking",
            ["Personalization"] = "Inking, typing, and handwriting data collection",
        };

    private const string TabKey = "privacy";

    private readonly DisplayModePreferencesStore? _displayModeStore;
    private bool _suppressModePersist;

    /// <summary>Registry Data display mode (10-2): shows raw paths and values on every card.</summary>
    [ObservableProperty]
    private bool _showRegistryData;

    /// <summary>Compact display mode (10-2): collapses card descriptions to a dense toggle list.</summary>
    [ObservableProperty]
    private bool _isCompact;

    public PrivacyViewModel(
        PrivacyScanData scanData,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService,
        DisplayModePreferencesStore? displayModeStore = null,
        ICapabilityDetector? capabilityDetector = null)
    {
        _displayModeStore = displayModeStore;
        // Factories re-read live state at stage time — a scan-time snapshot would bake
        // stale BeforeValues into the descriptors after the first apply.
        var provider = new PrivacyCardProvider(new PrivacySettingsReader(registryService));

        var cards = provider.BuildCards(scanData)
            .Select(source => new SettingCardViewModel(source, pendingChangesService, capabilityDetector))
            .ToList();

        foreach (var group in cards.GroupBy(c => c.Model.GroupId ?? string.Empty))
        {
            CardGroups.Add(new SettingCardGroupViewModel
            {
                Header = group.Key,
                Subtitle = SectionSubtitles.TryGetValue(group.Key, out var subtitle) ? subtitle : null,
                Cards = group.ToList(),
            });
        }

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
