namespace ThisIsMyPC.Modules.Annoyances.Models;

/// <summary>
/// Composite state of the two Bing-search registry values. They suppress in opposite
/// polarities: BingSearchEnabled 0 = suppressed, DisableSearchBoxSuggestions 1 = suppressed.
/// The toggle is considered suppressed only when BOTH values are in their suppressing state.
/// </summary>
public sealed record BingSearchState(
    string BingSearchEnabledValue,
    string DisableSearchBoxSuggestionsValue,
    bool IsSuppressed);
