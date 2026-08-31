using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// Cross-module search now lands on the matching card: selecting a result
/// pre-fills the module page's own search box (5-3 follow-through). Real
/// module scan, so Diagnostic.
/// </summary>
[Trait("Category", "Diagnostic")]
public class SearchFocusShotTests
{
    [AvaloniaFact]
    public async Task SelectingASearchResult_FiltersTheModulePageToThatCard()
    {
        using var session = UiSession.ForMainWindow("search-focus");
        var vm = (MainWindowViewModel)session.Window.DataContext!;

        var searchBox = session.Find<TextBox>(t => t.Watermark == "Search settings...");
        session.Type(searchBox, "copilot");
        await session.WaitForAsync(() => vm.HasSearchResults, what: "search results");
        session.Screenshot("results-open");

        session.ClickText("Disable Windows Copilot");
        await session.WaitForAsync(
            () => vm.CurrentContent is AnnoyancesViewModel { SearchText.Length: > 0 },
            what: "Annoyances page focused on the result");
        session.Screenshot("card-focused");

        var annoyances = (AnnoyancesViewModel)vm.CurrentContent!;
        Assert.Equal("Disable Windows Copilot", annoyances.SearchText);
        Assert.True(session.IsTextVisible("Disable Windows Copilot"));
    }
}
