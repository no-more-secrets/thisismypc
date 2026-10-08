using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Software.Services;
using ThisIsMyPC.Modules.Software.Models;

namespace ThisIsMyPC.App.UiTests;

public sealed class CatalogAdditionTests
{
    [AvaloniaFact]
    public void OfficialDownloadOpensWithoutStagingAnInstall()
    {
        var queue = new PendingActionsService();
        string? opened = null;
        var entry = SoftwareCatalog.Entries.Single(e => e.Id == "spotifast");
        var row = new SoftwareAppViewModel(entry, false, queue, openDownload: url => opened = url);
        Assert.Equal("Download", row.ActionButtonText);
        row.ToggleQueueCommand.Execute(null);
        Assert.Equal("https://spotifast.rocks/download/", opened);
        Assert.Equal(0, queue.PendingCount);
        Assert.False(row.IsQueued);
    }

    [AvaloniaFact]
    public void NewCatalogEntriesRenderAndClaudeQueuesInstall()
    {
        var queue = new PendingActionsService();
        var data = new SoftwareScanData(SoftwareCatalog.Entries, new HashSet<string>(), true, "test", [], new HashSet<string>(), true);
        var vm = new SoftwareViewModel(data, queue, new Fakes.UiFakeWingetService());
        using var session = UiSession.ForView(new SoftwareView(), vm, "catalog-additions", width: 950);
        Assert.Contains(vm.Categories, c => c == "Professional Tools");
        foreach (var search in new[] { "Spotifast", "Claude Code CLI", "Professional" })
        {
            vm.SearchText = search == "Professional" ? "" : search;
            vm.SelectedCategory = search == "Professional" ? "Professional Tools" : SoftwareViewModel.AllCategories;
            session.Pump();
            foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Dark, Avalonia.Styling.ThemeVariant.Light })
            {
                session.SetTheme(theme);
                session.Screenshot(search.Replace(" ", "-") + "-" + theme.Key);
            }
        }
        var entry = SoftwareCatalog.Entries.Single(e => e.Id == "claude-code");
        var row = new SoftwareAppViewModel(entry, false, queue);
        row.ToggleQueueCommand.Execute(null);
        Assert.Equal(1, queue.PendingCount);
        Assert.Equal("Anthropic.ClaudeCode", row.WingetId);
    }
}
