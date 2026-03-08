namespace ThisIsMyPC.App.ViewModels;

public enum ContextMenuTab
{
    File,
    Folder,
    FolderBackground,
    Desktop,
    Misc,
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
    public static IReadOnlyList<ContextMenuTab> GetTabs(string appliesTo) => appliesTo switch
    {
        "All files" or "All filesystem objects" => [ContextMenuTab.File],
        "Directories" or "Folders" => [ContextMenuTab.Folder],
        "Folder background" => [ContextMenuTab.FolderBackground, ContextMenuTab.Desktop],
        "Drives" => [ContextMenuTab.Misc],
        "This PC" => [ContextMenuTab.Misc],
        "Network" => [ContextMenuTab.Misc],
        "Recycle Bin" => [ContextMenuTab.Misc],
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
}
