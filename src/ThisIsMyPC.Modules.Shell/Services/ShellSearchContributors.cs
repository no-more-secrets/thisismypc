using ThisIsMyPC.Core.Search;

namespace ThisIsMyPC.Modules.Shell.Services;

// Curated static search inventories for the custom-view Shell-family modules (5-3).
// Card modules generate entries from their readers; these tabs predate the card system,
// so their headline controls are listed by hand.

public sealed class ExplorerSearchContributor : ISearchSettingsContributor
{
    public string ModuleId => "Explorer";

    public IReadOnlyList<SearchEntry> GetSearchEntries() =>
    [
        new(ModuleId, "taskbar-alignment", "Taskbar alignment",
            "Left-align or center the taskbar.", ["TaskbarAl", "taskbar"]),
        new(ModuleId, "taskbar-widgets", "Widgets button",
            "Show or hide the taskbar Widgets button.", ["TaskbarDa", "widgets"]),
        new(ModuleId, "classic-context-menu", "Classic (full) right-click menu",
            "Restore the Windows 10 style context menu.", ["InprocServer32", "context menu", "right click"]),
        new(ModuleId, "classic-command-bar", "Classic Explorer command bar",
            "Restore the classic ribbon-style command bar.", ["command bar", "ribbon"]),
        new(ModuleId, "file-extensions", "Show file extensions",
            "Always show file name extensions in Explorer.", ["HideFileExt", "extensions"]),
        new(ModuleId, "hidden-files", "Show hidden files",
            "Show hidden files and folders in Explorer.", ["Hidden", "hidden files"]),
    ];
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
