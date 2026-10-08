using ThisIsMyPC.Core.Search;

namespace ThisIsMyPC.Modules.Software.Services;

/// <summary>Catalog and Windows app destinations for the global search box.</summary>
public sealed class SoftwareSearchContributor : ISearchSettingsContributor
{
    public string ModuleId => SoftwareModule.ModuleName;

    public IReadOnlyList<SearchEntry> GetSearchEntries() => SoftwareCatalog.Entries
        .Select(entry => new SearchEntry(ModuleId, "catalog:" + entry.Id, entry.Name, entry.Description,
            entry.InstalledNames.Concat(entry.InstalledIds).Append(entry.WingetId).Append(entry.Category).ToArray()))
        .Concat(WindowsAppsCatalog.Entries.Select(entry => new SearchEntry(ModuleId, "appx:" + entry.Id,
            entry.Name, entry.Description, [entry.PackageId, entry.Category])))
        .ToArray();
}
