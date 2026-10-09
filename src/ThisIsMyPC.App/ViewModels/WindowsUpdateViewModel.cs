using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Models;
using ThisIsMyPC.Modules.WindowsUpdate.Services;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// Windows Update as a card-rendered page. The module's
/// WindowsUpdateCardProvider supplies SettingCardSources; the shared page VM
/// wraps them in interactive cards, one tab per provider section.
/// </summary>
public sealed class WindowsUpdateViewModel : SettingCardPageViewModel
{
    private static readonly IReadOnlyDictionary<string, string> SectionSubtitles =
        new Dictionary<string, string>
        {
            ["Update Behavior"] = "Update install timing, forced restarts, driver replacement, and feature-release pinning. Applied with the Update Orchestrator cache cleared so they stick",
            ["Delivery Optimization"] = "Choose which PCs can share updates",
            ["Update Experience"] = "How Windows Update looks and behaves while it runs",
        };

    public WindowsUpdateViewModel(
        WindowsUpdateScanData scanData,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService,
        DisplayModePreferencesStore? displayModeStore = null,
        ICapabilityDetector? capabilityDetector = null,
        Services.IOwnerModeLifecycle? ownerMode = null,
        Services.IUserFeedback? feedback = null,
        Core.Policies.PolicyControlStateReader? policyStates = null)
        : base(
            "windows-update",
            // Factories re-read live state at stage time; a scan-time snapshot would bake
            // stale BeforeValues into the descriptors after the first apply.
            new WindowsUpdateCardProvider(new WindowsUpdateSettingsReader(registryService), capabilityDetector, policyStates).BuildCards(scanData),
            SectionSubtitles,
            pendingChangesService,
            displayModeStore,
            capabilityDetector,
            ownerMode,
            feedback,
            policyStates ?? new Core.Policies.PolicyControlStateReader(registryService, capabilityDetector: capabilityDetector))
    {
    }
}
