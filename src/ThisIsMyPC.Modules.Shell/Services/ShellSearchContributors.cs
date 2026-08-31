using ThisIsMyPC.Core.Search;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Shell.Services;

// Search inventories for the custom-view Shell-family modules (5-3). Explorer
// preferences are derived from the live reader (like the card modules); the taskbar /
// classic-shell toggles predate the reader catalog, so they stay listed by hand.

public sealed class ExplorerSearchContributor : ISearchSettingsContributor
{
    private readonly ExplorerSettingsReader _reader;

    public ExplorerSearchContributor(IRegistryService registryService)
    {
        _reader = new ExplorerSettingsReader(registryService);
    }

    public string ModuleId => "Explorer";

    public IReadOnlyList<SearchEntry> GetSearchEntries()
    {
        var entries = _reader.ReadAll()
            .Select(p => new SearchEntry(
                ModuleId, p.Id, p.DisplayName, p.Description,
                [p.RegistryKeyPath, p.RegistryValueName]))
            .ToList();

        entries.Add(new SearchEntry(
            ModuleId, "taskbar-alignment", "Taskbar alignment",
            "Left-align or center the taskbar.", ["TaskbarAl", "taskbar"]));
        // Names must equal the Shell page row labels: search-to-card focus
        // pre-fills the page filter with the entry name, and a mismatch
        // filters the page empty.
        entries.Add(new SearchEntry(
            ModuleId, "taskbar-widgets", "Taskbar widgets",
            "Show or hide the taskbar Widgets button.", ["TaskbarDa", "widgets", "widgets button"]));
        entries.Add(new SearchEntry(
            ModuleId, "classic-context-menu", "Classic context menu",
            "Restore the Windows 10 style full right-click menu.", ["InprocServer32", "context menu", "right click"]));
        entries.Add(new SearchEntry(
            ModuleId, "classic-command-bar", "Use classic command bar",
            "Restore the classic ribbon-style command bar.", ["command bar", "ribbon"]));

        return entries;
    }
}

public sealed class ContextMenuSearchContributor : ISearchSettingsContributor
{
    public string ModuleId => "Context Menus";

    public IReadOnlyList<SearchEntry> GetSearchEntries() =>
    [
        new(ModuleId, "handlers", "Context menu handlers",
            "Enable/disable the shell extensions that add right-click menu items.",
            ["shellex", "ContextMenuHandlers", "shell extension", "right click"]),
        new(ModuleId, "static-verbs", "Static context menu verbs",
            "Registry-defined right-click commands per file type.", ["verbs", "shell", "open with"]),
        new(ModuleId, "orphans", "Orphaned handler cleanup",
            "Find and remove handlers whose DLLs no longer exist.", ["orphan", "cleanup"]),
    ];
}

public sealed class EnvironmentSearchContributor : ISearchSettingsContributor
{
    public string ModuleId => "Environment";

    public IReadOnlyList<SearchEntry> GetSearchEntries() =>
    [
        new(ModuleId, "path-editor", "PATH editor",
            "Edit the user and system PATH entries safely.", ["PATH", "environment variables"]),
        new(ModuleId, "env-vars", "Environment variables",
            "Create, edit, and delete user and system environment variables.",
            ["environment", "variables", "SETX"]),
    ];
}
