using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Shell.Changes;

namespace ThisIsMyPC.App.UiTests;

public class SearchDestinationShotTests
{
    [AvaloniaFact]
    public void ExplorerSearch_SelectsTheOwningTab_AfterManualTabChanges()
    {
        using var vm = new ShellViewModel(new ShellScanData(
            new ExplorerSettingsReader(new UiFakeRegistryService()).ReadAll(), new TaskbarSettings(1, true, false, false)),
            new PendingChangesService(), new UiFakeRegistryService());
        using var session = UiSession.ForView(new ShellView(), vm, "search-destinations");
        var tabs = session.Find<TabControl>(_ => true);
        vm.NavigateToSearchResult("taskbar-widgets", "Taskbar widgets");
        session.Pump();
        Assert.Equal(2, tabs.SelectedIndex);
        Assert.True(session.IsTextVisible("Taskbar widgets"));
        session.Screenshot("explorer-taskbar");
        session.ClickText("Desktop");
        Assert.Equal(3, vm.SelectedTabIndex);
        vm.NavigateToSearchResult("classic-context-menu", "Classic context menu");
        session.Pump();
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.True(session.IsTextVisible("Classic context menu"));

        foreach (var preference in new ExplorerSettingsReader(new UiFakeRegistryService()).ReadAll())
        {
            vm.NavigateToSearchResult(preference.Id, preference.DisplayName);
            session.Pump();
            var expected = preference.Section switch
            {
                ShellSection.General => 0, ShellSection.Taskbar => 2,
                ShellSection.Desktop => 3, ShellSection.StartMenu => 4, _ => 1,
            };
            Assert.Equal(expected, tabs.SelectedIndex);
        }
    }

    [AvaloniaFact]
    public void ExplorerPatcherSearch_SelectsFileExplorer_OrTheInstallerWhenAbsent()
    {
        foreach (var installed in new[] { true, false })
        {
            using var vm = new ShellViewModel(new ShellScanData([], new TaskbarSettings(1, true, false, false),
                installed ? [.. ExplorerPatcherCatalog.Entries.Select(e => e with { IsAvailable = e.Condition.Length == 0 })] : [],
                ExplorerPatcherInstalled: installed), new PendingChangesService(), new UiFakeRegistryService());
            using var session = UiSession.ForView(new ShellView(), vm, "search-destinations");
            vm.NavigateToSearchResult(ExplorerPatcherChangeFactory.SettingIdPrefix + "FileExplorerCommandUI", "Control Interface");
            session.Pump();
            Assert.Equal(installed ? 1 : 0, session.Find<TabControl>(_ => true).SelectedIndex);
            if (installed) Assert.True(session.IsTextVisible("Control Interface"));
            else Assert.Empty(vm.SearchText);
            session.Screenshot(installed ? "patcher-control-interface" : "patcher-absent");
        }
    }

    [AvaloniaFact]
    public void EnvironmentSearch_OpensVariablesOrPath_AndClearsAnOldFilter()
    {
        var vm = new EnvironmentViewModel(new EnvironmentScanData([], []), new PendingChangesService());
        using var session = UiSession.ForView(new EnvironmentView(), vm, "search-destinations");
        vm.VariableSearchText = "old filter";
        vm.NavigateToSearchResult("env-vars", "Environment variables");
        session.Pump();
        Assert.Equal(1, session.Find<TabControl>(_ => true).SelectedIndex);
        Assert.Empty(vm.VariableSearchText);
        session.Screenshot("environment-variables");
        vm.NavigateToSearchResult("path-editor", "PATH editor");
        session.Pump();
        Assert.Equal(0, session.Find<TabControl>(_ => true).SelectedIndex);
    }

    [AvaloniaFact]
    public void ContextMenuSearch_SelectsTheMatchingScope_WithoutLeavingHiddenFilters()
    {
        var file = new ContextMenuHandler("File extension", "file-id", @"HKCR\*\shellex\ContextMenuHandlers\Test",
            "Files", "file.dll", "Test", true);
        var orphan = new ContextMenuHandler("Missing folder extension", "orphan-id", @"HKCR\Directory\shellex\ContextMenuHandlers\Test",
            "Folders", "missing.dll", "Test", true, IsOrphaned: true);
        using var vm = new ContextMenuViewModel([file, orphan], new PendingChangesService(), new UiFakeRegistryService());
        using var session = UiSession.ForView(new ContextMenuView(), vm, "search-destinations");
        vm.NavigateToSearchResult("orphans", "Orphaned handler cleanup");
        session.Pump();
        Assert.Equal(2, session.Find<TabControl>(_ => true).SelectedIndex);
        Assert.False(vm.IsOrphanFilterActive);
        Assert.Null(vm.HandlerTypeFilter);
        Assert.Single(vm.FileHandlers);
        Assert.Single(vm.FolderHandlers);
        Assert.True(session.IsTextVisible("Missing folder extension"));
        session.Screenshot("context-orphan-tab");
    }
}
