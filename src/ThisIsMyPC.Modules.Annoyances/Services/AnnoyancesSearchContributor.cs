using ThisIsMyPC.Core.Search;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances.Changes;

namespace ThisIsMyPC.Modules.Annoyances.Services;

/// <summary>Search entries generated from the live setting inventory (5-3).</summary>
public sealed class AnnoyancesSearchContributor : ISearchSettingsContributor
{
    private readonly AnnoyancesSettingsReader _reader;

    public AnnoyancesSearchContributor(IRegistryService registryService)
    {
        _reader = new AnnoyancesSettingsReader(registryService);
    }

    public string ModuleId => AnnoyanceChangeFactory.ModuleId;

    public IReadOnlyList<SearchEntry> GetSearchEntries()
    {
        var entries = _reader.ReadAll()
            .Select(p => new SearchEntry(
                ModuleId, p.Id, p.DisplayName, p.Description,
                [p.RegistryKeyPath, p.RegistryValueName]))
            .ToList();

        entries.Add(new SearchEntry(
            ModuleId, "bing-search", "Disable Bing web search in Start Menu",
            "Start Menu search shows local results only.",
            ["BingSearchEnabled", "DisableSearchBoxSuggestions", "web search"]));
        entries.Add(new SearchEntry(
            ModuleId, "copilot", "Disable Windows Copilot",
            "Turns the Copilot assistant off by policy.",
            ["TurnOffWindowsCopilot", "AI", "assistant"]));
        entries.Add(new SearchEntry(
            ModuleId, "recall", "Disable Windows Recall and AI data analysis",
            "Blocks Recall snapshots and AI activity analysis.",
            ["AllowRecallEnablement", "DisableAIDataAnalysis", "snapshots", "AI"]));
        entries.Add(new SearchEntry(
            ModuleId, "settings-suggested-content", "Suppress suggested content in Settings",
            "Removes ad-like suggestion tiles from the Settings app.",
            ["ContentDeliveryManager", "SubscribedContent", "ads"]));

        return entries;
    }
}
