using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Privacy.Models;
using ThisIsMyPC.Modules.Privacy.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Privacy &amp; Telemetry as a card-rendered page. The module's
/// PrivacyCardProvider supplies SettingCardSources; the shared page VM wraps
/// them in interactive cards, one tab per provider section.
/// </summary>
public sealed class PrivacyViewModel : SettingCardPageViewModel
{
    private static readonly IReadOnlyDictionary<string, string> SectionSubtitles =
        new Dictionary<string, string>
        {
            ["Diagnostic Data"] = "Diagnostic data collection and crash reporting",
            ["Permissions & Tracking"] = "Location, app launch tracking, clipboard sync, and online speech",
            ["Personalization"] = "Inking, typing, and handwriting data collection",
        };

    public PrivacyViewModel(
        PrivacyScanData scanData,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService,
        DisplayModePreferencesStore? displayModeStore = null,
        ICapabilityDetector? capabilityDetector = null,
        Services.IOwnerModeLifecycle? ownerMode = null)
        : base(
            "privacy",
            // Factories re-read live state at stage time; a scan-time snapshot would bake
            // stale BeforeValues into the descriptors after the first apply.
            new PrivacyCardProvider(new PrivacySettingsReader(registryService)).BuildCards(scanData),
            SectionSubtitles,
            pendingChangesService,
            displayModeStore,
            capabilityDetector,
            ownerMode)
    {
    }
}
