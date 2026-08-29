using System.Collections.ObjectModel;
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

    public AnnoyancesViewModel(
        AnnoyancesScanData scanData,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService)
    {
        // Factories re-read live state at stage time — a scan-time snapshot would bake
        // stale BeforeValues into the descriptors after the first apply.
        var provider = new AnnoyancesCardProvider(new AnnoyancesSettingsReader(registryService));

        var cards = provider.BuildCards(scanData)
            .Select(source => new SettingCardViewModel(source, pendingChangesService))
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
