namespace ThisIsMyPC.App.ViewModels;

/// <summary>The selected category to retain when a page is rescanned.</summary>
public interface ITabbedPage
{
    int SelectedTabIndex { get; set; }
}
