using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.ViewModels;

public enum ContextMenuTab
{
    File,
    Folder,
    FolderBackground,
    Desktop,
    Misc,
    Multi,
    PerFileType,
}

public enum MiscSurfaceGroup
{
    Drive,
    ThisPc,
    Network,
    RecycleBin,
}

public static class ContextMenuTabMapper
{
    public static IReadOnlyList<ContextMenuTab> GetTabs(string appliesTo, IReadOnlySet<ContextMenuSurface>? visibleSurfaces = null)
        => appliesTo switch
        {
            "All files" or "All filesystem objects" => [ContextMenuTab.File],
            "Directories" or "Folders" => [ContextMenuTab.Folder],
            "Folder background" => MapFolderBackground(visibleSurfaces),
            "Desktop background" => [ContextMenuTab.Desktop],
            "Drives" or "This PC" or "Network" or "Recycle Bin"
                or "Audio folders" or "Video folders" => [ContextMenuTab.Misc],
            _ => [ContextMenuTab.File],
        };

    public static IReadOnlyList<ContextMenuTab> GetTabsForModernScope(string scope)
        => scope switch
        {
            "All files" or "All filesystem objects" => [ContextMenuTab.File],
            "Directories" or "Folders" => [ContextMenuTab.Folder],
            // Modern handlers: Directory\Background does NOT inherit to Desktop
            // (AppModel State Repository uses strict literal matching)
            "Folder background" => [ContextMenuTab.FolderBackground],
            "Desktop background" => [ContextMenuTab.Desktop],
            "Drives" or "This PC" or "Network" or "Recycle Bin" => [ContextMenuTab.Misc],
            "Unknown scope" => [ContextMenuTab.Misc],
            _ => [ContextMenuTab.File],
        };

    public static IReadOnlyList<ContextMenuTab> GetTabsForStaticVerbScope(string scope)
        => scope switch
        {
            "All files" or "All filesystem objects" => [ContextMenuTab.File],
            "Directories" => [ContextMenuTab.Folder],
            "Folders" => [ContextMenuTab.Folder],
            // Static verbs at Directory\Background always appear on both folder backgrounds
            // and the desktop — unlike COM handlers, there is no probe filtering mechanism.
            "Folder background" => [ContextMenuTab.FolderBackground, ContextMenuTab.Desktop],
            "Desktop background" => [ContextMenuTab.Desktop],
            "Drives" => [ContextMenuTab.Misc],
            _ => [ContextMenuTab.File],
        };

    public static MiscSurfaceGroup? GetMiscGroup(string appliesTo) => appliesTo switch
    {
        "Drives" => MiscSurfaceGroup.Drive,
        "This PC" => MiscSurfaceGroup.ThisPc,
        "Network" => MiscSurfaceGroup.Network,
        "Recycle Bin" => MiscSurfaceGroup.RecycleBin,
        _ => null,
    };

    private static IReadOnlyList<ContextMenuTab> MapFolderBackground(IReadOnlySet<ContextMenuSurface>? visibleSurfaces)
    {
        if (visibleSurfaces is null)
            return [ContextMenuTab.FolderBackground, ContextMenuTab.Desktop];

        var tabs = new List<ContextMenuTab>(2);
        if (visibleSurfaces.Contains(ContextMenuSurface.FolderBackground))
            tabs.Add(ContextMenuTab.FolderBackground);
        if (visibleSurfaces.Contains(ContextMenuSurface.DesktopBackground))
            tabs.Add(ContextMenuTab.Desktop);

        // Safe fallback: if probe returned empty set, show on both
        return tabs.Count > 0 ? tabs : [ContextMenuTab.FolderBackground, ContextMenuTab.Desktop];
    }
}
