using System.Collections.ObjectModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Models;

namespace ThisIsMyPC.App.ViewModels;

public partial class AnnoyancesViewModel : ViewModelBase
{
    public ObservableCollection<ShellSettingViewModel> ScoobeAndWelcomeSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> BingAndEdgeSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> AdvertisingAndTrackingSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> GamingAndAccessibilitySettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> AiFeaturesSettings { get; } = [];

    public AnnoyancesViewModel(
        AnnoyancesScanData scanData,
        IPendingChangesService pendingChangesService,
        IRegistryService registryService)
    {
        // Factories re-read live state at stage time — a scan-time snapshot would bake
        // stale BeforeValues into the descriptors after the first apply.
        var liveReader = new Modules.Annoyances.Services.AnnoyancesSettingsReader(registryService);

        foreach (var pref in scanData.Preferences.Where(p => p.Section == AnnoyanceSection.ScoobeAndWelcome))
        {
            var captured = pref;
            ScoobeAndWelcomeSettings.Add(new ShellSettingViewModel(
                label: captured.DisplayName,
                description: captured.Description,
                systemPath: $@"{captured.RegistryKeyPath}\{captured.RegistryValueName}",
                isEnabled: captured.IsSuppressed,
                pendingChangesService: pendingChangesService,
                changeFactory: suppress => AnnoyanceChangeFactory.CreateToggle(ReadLive(liveReader, captured.Id), suppress),
                readRegistryState: () => ReadLive(liveReader, captured.Id).IsSuppressed));
        }
        BingAndEdgeSettings.Add(new ShellSettingViewModel(
            label: "Disable Bing web search in Start Menu",
            description: "Start Menu search stops sending your queries to Bing and shows local results only. Windows Update and Web Experience Pack deployments are known to revert this (requires Explorer restart).",
            systemPath: $@"{Modules.Annoyances.AnnoyancesRegistryPaths.SearchKeyPath}\BingSearchEnabled",
            isEnabled: scanData.BingSearch.IsSuppressed,
            pendingChangesService: pendingChangesService,
            groupFactory: suppress => AnnoyanceChangeFactory.CreateBingSearchToggle(liveReader.ReadBingSearch(), suppress),
            readRegistryState: () => liveReader.ReadBingSearch().IsSuppressed));

        foreach (var pref in scanData.Preferences.Where(p => p.Section == AnnoyanceSection.BingAndEdge))
        {
            var captured = pref;
            BingAndEdgeSettings.Add(new ShellSettingViewModel(
                label: captured.DisplayName,
                description: captured.Description,
                systemPath: $@"{captured.RegistryKeyPath}\{captured.RegistryValueName}",
                isEnabled: captured.IsSuppressed,
                pendingChangesService: pendingChangesService,
                changeFactory: suppress => AnnoyanceChangeFactory.CreateDriftFragileToggle(ReadLive(liveReader, captured.Id), suppress),
                readRegistryState: () => ReadLive(liveReader, captured.Id).IsSuppressed));
        }

        foreach (var pref in scanData.Preferences.Where(p => p.Section == AnnoyanceSection.AdvertisingAndTracking))
        {
            var captured = pref;
            AdvertisingAndTrackingSettings.Add(new ShellSettingViewModel(
                label: captured.DisplayName,
                description: captured.Description,
                systemPath: $@"{captured.RegistryKeyPath}\{captured.RegistryValueName}",
                isEnabled: captured.IsSuppressed,
                pendingChangesService: pendingChangesService,
                changeFactory: suppress => AnnoyanceChangeFactory.CreateToggle(ReadLive(liveReader, captured.Id), suppress),
                readRegistryState: () => ReadLive(liveReader, captured.Id).IsSuppressed));
        }

        // Settings suggested content: one toggle, three CDM values in one atomic group
        const string suggestedContentDescription =
            "Stops the ad-like \"suggested content\" tiles Microsoft injects into the Settings app. "
            + "The comprehensive privacy suite with companion service management arrives in the Privacy & Telemetry module.";
        AdvertisingAndTrackingSettings.Add(new ShellSettingViewModel(
            label: "Suppress suggested content in Settings",
            description: suggestedContentDescription,
            systemPath: $@"{Modules.Annoyances.AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath}\SubscribedContent-338393Enabled",
            isEnabled: scanData.SettingsSuggestedContent.All(p => p.IsSuppressed),
            pendingChangesService: pendingChangesService,
            groupFactory: suppress => AnnoyanceChangeFactory.CreateGroupToggle(
                liveReader.ReadSettingsSuggestedContent(),
                settingId: "settings-suggested-content",
                displayName: "Suggested content in Settings",
                description: suggestedContentDescription,
                suppress),
            readRegistryState: () => liveReader.ReadSettingsSuggestedContent().All(p => p.IsSuppressed)));

        foreach (var pref in scanData.Preferences.Where(p => p.Section == AnnoyanceSection.GamingAndAccessibility))
        {
            var captured = pref;
            GamingAndAccessibilitySettings.Add(new ShellSettingViewModel(
                label: captured.DisplayName,
                description: captured.Description,
                systemPath: $@"{captured.RegistryKeyPath}\{captured.RegistryValueName}",
                isEnabled: captured.IsSuppressed,
                pendingChangesService: pendingChangesService,
                changeFactory: suppress => AnnoyanceChangeFactory.CreateToggle(ReadLive(liveReader, captured.Id), suppress),
                readRegistryState: () => ReadLive(liveReader, captured.Id).IsSuppressed));
        }

        // Windows Copilot: one toggle, machine + user policy scope in one atomic group
        AiFeaturesSettings.Add(new ShellSettingViewModel(
            label: "Disable Windows Copilot",
            description: "Turns the Windows Copilot assistant off by policy, in both machine and user scope. "
                + "Windows feature updates and Copilot app deployments are known to bring Copilot surfaces back (requires Explorer restart).",
            systemPath: $@"{Modules.Annoyances.AnnoyancesRegistryPaths.CopilotMachinePoliciesKeyPath}\TurnOffWindowsCopilot",
            isEnabled: scanData.CopilotPolicy.All(p => p.IsSuppressed),
            pendingChangesService: pendingChangesService,
            groupFactory: suppress => AnnoyanceChangeFactory.CreateCopilotPolicyToggle(liveReader.ReadCopilotPolicy(), suppress),
            readRegistryState: () => liveReader.ReadCopilotPolicy().All(p => p.IsSuppressed)));

        AddAiFeatureSingle(scanData, pendingChangesService, liveReader, "copilot-button");

        // Recall: one toggle, three WindowsAI policy values in one atomic group
        const string recallDescription =
            "Blocks Windows Recall from taking and saving screen snapshots and turns off AI analysis of your activity. "
            + "On PCs without Copilot+ hardware these policies are inert today; setting them future-proofs the machine.";
        AiFeaturesSettings.Add(new ShellSettingViewModel(
            label: "Disable Windows Recall and AI data analysis",
            description: recallDescription,
            systemPath: $@"{Modules.Annoyances.AnnoyancesRegistryPaths.WindowsAiPoliciesKeyPath}\AllowRecallEnablement",
            isEnabled: scanData.Recall.All(p => p.IsSuppressed),
            pendingChangesService: pendingChangesService,
            groupFactory: suppress => AnnoyanceChangeFactory.CreateGroupToggle(
                liveReader.ReadRecall(),
                settingId: "recall",
                displayName: "Windows Recall and AI data analysis",
                description: recallDescription,
                suppress),
            readRegistryState: () => liveReader.ReadRecall().All(p => p.IsSuppressed)));

        AddAiFeatureSingle(scanData, pendingChangesService, liveReader, "edge-sidebar");
    }

    // AC ordering (Copilot policy, button, Recall, Edge sidebar) interleaves singles with
    // groups, so the AiFeatures singles are added by id instead of a section loop.
    private void AddAiFeatureSingle(
        AnnoyancesScanData scanData,
        IPendingChangesService pendingChangesService,
        Modules.Annoyances.Services.AnnoyancesSettingsReader liveReader,
        string id)
    {
        var pref = scanData.Preferences.Single(p => p.Id == id);
        AiFeaturesSettings.Add(new ShellSettingViewModel(
            label: pref.DisplayName,
            description: pref.Description,
            systemPath: $@"{pref.RegistryKeyPath}\{pref.RegistryValueName}",
            isEnabled: pref.IsSuppressed,
            pendingChangesService: pendingChangesService,
            changeFactory: suppress => AnnoyanceChangeFactory.CreateToggle(ReadLive(liveReader, id), suppress),
            readRegistryState: () => ReadLive(liveReader, id).IsSuppressed));
    }

    private static AnnoyancePreference ReadLive(
        Modules.Annoyances.Services.AnnoyancesSettingsReader reader, string id)
        => reader.ReadAll().Single(p => p.Id == id);
}
