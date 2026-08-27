using System.Collections.ObjectModel;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Models;

namespace ThisIsMyPC.App.ViewModels;

public partial class AnnoyancesViewModel : ViewModelBase
{
    public ObservableCollection<ShellSettingViewModel> ScoobeAndWelcomeSettings { get; } = [];
    public ObservableCollection<ShellSettingViewModel> BingAndEdgeSettings { get; } = [];

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
    }

    private static AnnoyancePreference ReadLive(
        Modules.Annoyances.Services.AnnoyancesSettingsReader reader, string id)
        => reader.ReadAll().Single(p => p.Id == id);
}
