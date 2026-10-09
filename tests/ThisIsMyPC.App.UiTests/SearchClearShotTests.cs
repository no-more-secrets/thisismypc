using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.App.Controls;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.UiTests;

public sealed class SearchClearShotTests
{
    [AvaloniaFact]
    public void ClearUpdatesBindingRestoresFocusAndSupportsKeyboard()
    {
        var model = new SearchModel();
        var search = new SearchTextBox { Watermark = "Search settings", Margin = new Thickness(24), Width = 360 };
        search.Bind(TextBox.TextProperty, new Binding(nameof(SearchModel.Query)) { Source = model, Mode = BindingMode.TwoWay });
        using var session = UiSession.ForView(new UserControl { Content = search }, model, "search-clear", width: 440, height: 120);
        var clear = (Button)((Border)search.InnerRightContent!).Child!;
        Assert.False(clear.IsVisible);
        Assert.Equal("Clear search", AutomationProperties.GetName(clear));
        session.Type(search, "update");
        Assert.True(clear.IsVisible);
        session.Screenshot("dark-filled");
        session.Click(clear);
        Assert.Equal("", model.Query);
        Assert.True(search.IsFocused);
        Assert.False(clear.IsVisible);
        session.Screenshot("dark-empty");
        model.Query = "network";
        session.Pump();
        Assert.Equal("network", search.Text);
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("light-filled");
        clear.Focus();
        session.Window.KeyPress(Key.Enter, RawInputModifiers.None);
        session.Window.KeyRelease(Key.Enter, RawInputModifiers.None);
        session.Pump();
        Assert.Equal("", model.Query);
        Assert.True(search.IsFocused);
        session.Type(search, "new search");
        Assert.Equal("new search", model.Query);
        search.IsReadOnly = true;
        Assert.False(clear.IsVisible);
    }

    [AvaloniaFact]
    [Trait("Category", "Diagnostic")]
    public async Task GlobalAndModuleSearchClearTheirResults()
    {
        using var session = UiSession.ForMainWindow("search-clear-live");
        var main = (MainWindowViewModel)session.Window.DataContext!;
        var global = session.Find<SearchTextBox>(s => s.Name == "SearchBox");
        session.Type(global, "copilot");
        await session.WaitForAsync(() => main.HasSearchResults, what: "global search results");
        session.Screenshot("global-filled");
        session.Click((Button)((Border)global.InnerRightContent!).Child!);
        await session.WaitForAsync(() => !main.HasSearchResults && main.SearchQuery == "", what: "global search cleared");
        session.OpenModule("Windows Annoyances");
        await session.WaitForAsync(() => main.CurrentContent is AnnoyancesViewModel, what: "annoyances page");
        var local = session.Find<SearchTextBox>(s => s.Watermark == "Search settings");
        session.Type(local, "copilot");
        Assert.True(((Button)((Border)local.InnerRightContent!).Child!).IsVisible, "Clear button must be visible for module search.");
        Assert.True(((Button)((Border)local.InnerRightContent!).Child!).Bounds.Width > 0, "Clear button must have a mouse target.");
        session.Screenshot("module-filled");
        var clearPosition = ((Button)((Border)local.InnerRightContent!).Child!).TranslatePoint(default, local);
        Assert.True(clearPosition?.X < local.Bounds.Width, $"Clear button at {clearPosition}; search bounds {local.Bounds}.");
        session.Click((Button)((Border)local.InnerRightContent!).Child!);
        Assert.Equal("", ((AnnoyancesViewModel)main.CurrentContent!).SearchText);
        Assert.True(local.IsFocused);
        session.Screenshot("module-cleared");
    }

    private sealed class SearchModel : ObservableObject
    {
        private string _query = "";
        public string Query { get => _query; set => SetProperty(ref _query, value); }
    }
}
