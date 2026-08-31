namespace ThisIsMyPC.App.ViewModels;

/// <summary>
/// A module page whose in-page search box can focus a cross-module search result
/// (5-3): navigation pre-fills SearchText with the result name so the matching
/// card or row is the page's visible content. Implement only where search entry
/// names mirror the page's row display names; a page-level entry would filter
/// the page empty instead.
/// </summary>
public interface ISearchFocusTarget
{
    string SearchText { get; set; }
}
