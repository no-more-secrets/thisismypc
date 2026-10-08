using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Software.Models;
using ThisIsMyPC.Modules.Software.Services;

namespace ThisIsMyPC.App.UiTests;

public sealed class SoftwareAnnotationShotTests
{
    [AvaloniaFact]
    public async Task SearchDestinationsResetFilters_AndBothListsShowArtwork()
    {
        var scan = new SoftwareScanData(SoftwareCatalog.Entries, new HashSet<string> { "OpenAI.Codex" }, true, "test",
            WindowsAppsCatalog.Entries, new HashSet<string>(), true)
        { ExternallyManagedIds = new HashSet<string> { "OpenAI.Codex" } };
        var queue = new PendingActionsService();
        using var vm = new SoftwareViewModel(scan, queue, new Fakes.UiFakeWingetService(), new FakeIcons());
        using var session = UiSession.ForView(new SoftwareView(), vm, "software-annotations", width: 950, height: 676);
        vm.SelectedCategory = "Browsers";
        vm.SelectedInstallStateFilter = "Installed";
        vm.NavigateToSearchResult("catalog:inkscape", "Inkscape");
        session.Pump();
        Assert.Equal("Inkscape", Assert.Single(vm.FilteredApps).Name);
        session.Screenshot("inkscape");
        vm.NavigateToSearchResult("catalog:codex", "Codex CLI");
        var codex = Assert.Single(vm.FilteredApps);
        Assert.True(codex.IsInstalled);
        Assert.False(codex.CanAct);
        codex.ToggleQueueCommand.Execute(null);
        Assert.Empty(queue.PendingActions);
        Assert.Equal("Installed", codex.ActionButtonText);
        await session.WaitForAsync(() => !vm.IsUpdatesLoading && vm.WindowsApps.All(a => a.Artwork.HasIcon));
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            vm.SelectedTabIndex = 1;
            session.Pump();
            Assert.All(vm.Updates, row => Assert.True(row.Artwork.HasIcon));
            Assert.NotEmpty(session.FindAll<Image>(i => i.Source is not null && i.IsEffectivelyVisible));
            session.Screenshot($"updates-{theme.Key}");
            vm.NavigateToSearchResult("appx:calculator", "Calculator");
            session.Pump();
            Assert.Equal(2, vm.SelectedTabIndex);
            Assert.NotEmpty(vm.FilteredWindowsApps);
            Assert.NotEmpty(session.FindAll<Image>(i => i.Source is not null && i.IsEffectivelyVisible));
            session.Screenshot($"windows-apps-{theme.Key}");
        }
    }

    [AvaloniaFact]
    [Trait("Category", "Diagnostic")]
    public async Task GlobalAppSearchNavigates_AndStatusGapDoubles()
    {
        using var session = UiSession.ForMainWindow("software-global-search");
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => vm.SidebarGroups.Count > 0);
        var search = session.Find<TextBox>(b => b.Name == "SearchBox");
        session.Type(search, "Inkscape");
        await session.WaitForAsync(() => vm.SearchResults.Count > 0);
        session.Screenshot("results");
        session.ClickText("Inkscape");
        await session.WaitForAsync(() => vm.CurrentContent is SoftwareViewModel { SearchText: "Inkscape" });
        Assert.True(session.IsTextVisible("Inkscape"));
        vm.StatusMessage = "Changes applied successfully";
        session.Pump();
        var message = session.Find<TextBlock>(t => t.Text == vm.StatusMessage);
        var count = session.Find<Button>(b => b.Classes.Contains("apply-bar-count"));
        var gap = message.TranslatePoint(default, session.Window)!.Value.X
            - (count.TranslatePoint(default, session.Window)!.Value.X + count.Bounds.Width);
        Assert.Equal(24, gap, 0.5);
        session.Screenshot("destination-status-gap");
    }

    private sealed class FakeIcons : ISoftwareIconProvider
    {
        public Task<Bitmap?> ReadAsync(string packageId, string displayName, bool appx)
            => Task.FromResult(SoftwareIcons.Get("inkscape"));
    }
}
