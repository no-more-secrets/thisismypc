using ThisIsMyPC.Core.Search;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>One row in the sidebar search results (5-3).</summary>
public sealed class SearchResultViewModel
{
    public SearchResultViewModel(SearchResult result)
    {
        Name = result.Entry.DisplayName;
        Description = result.Entry.Description;
        ModuleId = result.Entry.ModuleId;
        IsAvailable = result.ModuleAvailable;
        ModuleLine = result.ModuleAvailable
            ? result.Entry.ModuleId
            : $"{result.Entry.ModuleId} - unavailable: {result.UnavailableReason ?? "reason unknown"}";
    }

    public string Name { get; }
    public string Description { get; }
    public string ModuleId { get; }
    public string ModuleLine { get; }
    public bool IsAvailable { get; }
}
