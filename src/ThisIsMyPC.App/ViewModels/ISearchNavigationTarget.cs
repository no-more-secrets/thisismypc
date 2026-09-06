namespace ThisIsMyPC.App.ViewModels;

/// <summary>Opens the tab or nested page that owns a global search result.</summary>
public interface ISearchNavigationTarget
{
    void NavigateToSearchResult(string settingId, string displayName);
}
