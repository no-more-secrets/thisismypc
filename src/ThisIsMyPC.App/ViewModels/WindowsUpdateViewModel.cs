using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Models;
using ThisIsMyPC.Modules.WindowsUpdate.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Card-rendered Windows Update module tab (Epic 10 pattern, mirrors
/// AnnoyancesViewModel). The module's WindowsUpdateCardProvider supplies
/// SettingCardSources; this VM wraps them in interactive card VMs grouped by section.
/// </summary>
public partial class WindowsUpdateViewModel : ViewModelBase, IDisposable
{
    public ObservableCollection<SettingCardGroupViewModel> CardGroups { get; } = [];

    private static readonly IReadOnlyDictionary<string, string> SectionSubtitles =
        new Dictionary<string, string>
        {
            ["Update Behavior"] = "Update install timing, forced restarts, driver replacement, and feature-release pinning — policies applied with the Update Orchestrator's cache cleared so they actually stick",
            ["Delivery Optimization"] = "Stop update peer-to-peer sharing from consuming background bandwidth",
        };

    private const string TabKey = "windows-update";

    private readonly DisplayModePreferencesStore? _displayModeStore;
    private bool _suppressModePersist;

    /// <summary>Registry Data display mode (10-2): shows raw paths and values on every card.</summary>
    [ObservableProperty]
    private bool _showRegistryData;

    /// <summary>Compact display mode (10-2): collapses card descriptions to a dense toggle list.</summary>
    [ObservableProperty]
    private bool _isCompact;

    public WindowsUpdateViewModel(
        WindowsUpdateScanData scanData,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService,
        DisplayModePreferencesStore? displayModeStore = null,
        ICapabilityDetector? capabilityDetector = null)
    {
        _displayModeStore = displayModeStore;
        // Factories re-read live state at stage time — a scan-time snapshot would bake
        // stale BeforeValues into the descriptors after the first apply.
        var provider = new WindowsUpdateCardProvider(new WindowsUpdateSettingsReader(registryService));

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
