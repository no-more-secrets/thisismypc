using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances.Models;
using ThisIsMyPC.Modules.Annoyances.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Windows Annoyances, the reference card-rendered page. The module's
/// AnnoyancesCardProvider supplies SettingCardSources; the shared page VM
/// wraps them in interactive cards, one tab per provider section.
/// </summary>
public sealed class AnnoyancesViewModel : SettingCardPageViewModel
{
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
        IRegistryService registryService,
        DisplayModePreferencesStore? displayModeStore = null,
        ICapabilityDetector? capabilityDetector = null,
        Services.IOwnerModeLifecycle? ownerMode = null)
        : base(
            "annoyances",
            // Factories re-read live state at stage time; a scan-time snapshot would bake
            // stale BeforeValues into the descriptors after the first apply.
            new AnnoyancesCardProvider(new AnnoyancesSettingsReader(registryService)).BuildCards(scanData),
            SectionSubtitles,
            pendingChangesService,
            displayModeStore,
            capabilityDetector,
            ownerMode)
    {
    }
}
