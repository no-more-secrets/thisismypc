using ThisIsMyPC.Core.Search;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Changes;

namespace ThisIsMyPC.Modules.WindowsUpdate.Services;

/// <summary>Search entries generated from the policy inventory (5-3).</summary>
public sealed class WindowsUpdateSearchContributor : ISearchSettingsContributor
{
    private readonly WindowsUpdateSettingsReader _reader;

    public WindowsUpdateSearchContributor(IRegistryService registryService)
    {
        _reader = new WindowsUpdateSettingsReader(registryService);
    }

    public string ModuleId => WindowsUpdateChangeFactory.ModuleId;

    public IReadOnlyList<SearchEntry> GetSearchEntries()
    {
        var entries = _reader.ReadSingles()
            .Select(s => new SearchEntry(
                ModuleId, s.Id, s.DisplayName, s.Description,
                [s.RegistryKeyPath, s.RegistryValueName]))
            .ToList();

        entries.Add(new SearchEntry(
            ModuleId, "version-pin", "Stay on the current Windows version",
            "Pins the machine to its current feature release until you remove the pin.",
            ["TargetReleaseVersion", "feature update", "upgrade", "24H2", "25H2"]));

        return entries;
    }
}
