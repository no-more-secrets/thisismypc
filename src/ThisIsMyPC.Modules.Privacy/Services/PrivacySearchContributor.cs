using ThisIsMyPC.Core.Search;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Privacy.Changes;

namespace ThisIsMyPC.Modules.Privacy.Services;

/// <summary>Search entries generated from the live setting inventory (5-3).</summary>
public sealed class PrivacySearchContributor : ISearchSettingsContributor
{
    private readonly PrivacySettingsReader _reader;

    public PrivacySearchContributor(IRegistryService registryService)
    {
        _reader = new PrivacySettingsReader(registryService);
    }

    public string ModuleId => PrivacyChangeFactory.ModuleId;

    public IReadOnlyList<SearchEntry> GetSearchEntries()
    {
        var entries = _reader.ReadSingles()
            .Select(p => new SearchEntry(
                ModuleId, p.Id, p.DisplayName, p.Description,
                [p.RegistryKeyPath, p.RegistryValueName]))
            .ToList();

        entries.Add(new SearchEntry(
            ModuleId, "inking-typing", "Disable inking and typing personalization",
            "Stops handwriting, typing history, and contact collection.",
            ["InputPersonalization", "HarvestContacts", "AcceptedPrivacyPolicy", "typing", "handwriting"]));

        return entries;
    }
}
