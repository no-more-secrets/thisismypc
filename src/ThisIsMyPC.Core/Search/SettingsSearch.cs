namespace ThisIsMyPC.Core.Search;

/// <summary>One searchable setting/control (5-3).</summary>
public sealed record SearchEntry(
    string ModuleId,
    string SettingId,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Keywords);

/// <summary>
/// Modules expose their searchable inventory by implementing this and registering it
/// in DI (explicit AddSingleton, like ISetEntryInspector).
/// </summary>
public interface ISearchSettingsContributor
{
    /// <summary>The module's IModule.Info.Name string.</summary>
    string ModuleId { get; }

    IReadOnlyList<SearchEntry> GetSearchEntries();
}

public sealed record SearchResult(
    SearchEntry Entry,
    bool ModuleAvailable,
    string? UnavailableReason);

/// <summary>
/// Case-insensitive cross-module settings search. Rank: display-name hit first, then
/// keyword (registry paths, service names), then description. Entries from unavailable
/// modules are included and marked (discoverability per FR11).
/// </summary>
public sealed class SettingsSearchService
{
    private readonly IReadOnlyList<ISearchSettingsContributor> _contributors;
    private readonly Func<string, (bool IsAvailable, string? Reason)> _availabilityLookup;

    public const int MaxResults = 15;

    public SettingsSearchService(
        IEnumerable<ISearchSettingsContributor> contributors,
        Func<string, (bool IsAvailable, string? Reason)> availabilityLookup)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        ArgumentNullException.ThrowIfNull(availabilityLookup);
        _contributors = contributors.ToList();
        _availabilityLookup = availabilityLookup;
    }

    public IReadOnlyList<SearchResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return [];

        var needle = query.Trim();
        var ranked = new List<(int Rank, SearchResult Result)>();

        foreach (var contributor in _contributors)
        {
            var (available, reason) = _availabilityLookup(contributor.ModuleId);
            foreach (var entry in contributor.GetSearchEntries())
            {
                var rank = RankOf(entry, needle);
                if (rank < int.MaxValue)
                    ranked.Add((rank, new SearchResult(entry, available, available ? null : reason)));
            }
        }

        return ranked
            .OrderBy(r => r.Rank)
            .ThenBy(r => r.Result.Entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)
            .Select(r => r.Result)
            .ToList();
    }

    private static int RankOf(SearchEntry entry, string needle)
    {
        if (entry.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (entry.Keywords.Any(k => k.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            return 1;
        if (entry.Description.Contains(needle, StringComparison.OrdinalIgnoreCase))
            return 2;
        return int.MaxValue;
    }
}
