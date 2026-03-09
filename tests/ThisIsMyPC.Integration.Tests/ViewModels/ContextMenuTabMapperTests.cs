using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Interop.Com.Shell;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class ContextMenuTabMapperTests
{
    [Theory]
    [InlineData("All files", ContextMenuTab.File)]
    [InlineData("All filesystem objects", ContextMenuTab.File)]
    public void GetTabs_file_surfaces(string appliesTo, ContextMenuTab expectedTab)
    {
        var tabs = ContextMenuTabMapper.GetTabs(appliesTo);
        Assert.Contains(expectedTab, tabs);
    }

    [Theory]
    [InlineData("Directories", ContextMenuTab.Folder)]
    [InlineData("Folders", ContextMenuTab.Folder)]
    public void GetTabs_folder_surfaces(string appliesTo, ContextMenuTab expectedTab)
    {
        var tabs = ContextMenuTabMapper.GetTabs(appliesTo);
        Assert.Contains(expectedTab, tabs);
    }

    [Fact]
    public void GetTabs_folder_background_maps_to_both_tabs_when_no_probe_data()
    {
        var tabs = ContextMenuTabMapper.GetTabs("Folder background");
        Assert.Contains(ContextMenuTab.FolderBackground, tabs);
        Assert.Contains(ContextMenuTab.Desktop, tabs);
    }

    [Fact]
    public void GetTabs_folder_background_uses_probe_data_folder_only()
    {
        var surfaces = new HashSet<ContextMenuSurface> { ContextMenuSurface.FolderBackground };
        var tabs = ContextMenuTabMapper.GetTabs("Folder background", surfaces);
        Assert.Contains(ContextMenuTab.FolderBackground, tabs);
        Assert.DoesNotContain(ContextMenuTab.Desktop, tabs);
    }

    [Fact]
    public void GetTabs_folder_background_uses_probe_data_desktop_only()
    {
        var surfaces = new HashSet<ContextMenuSurface> { ContextMenuSurface.DesktopBackground };
        var tabs = ContextMenuTabMapper.GetTabs("Folder background", surfaces);
        Assert.DoesNotContain(ContextMenuTab.FolderBackground, tabs);
        Assert.Contains(ContextMenuTab.Desktop, tabs);
    }

    [Fact]
    public void GetTabs_folder_background_uses_probe_data_both_surfaces()
    {
        var surfaces = new HashSet<ContextMenuSurface>
        {
            ContextMenuSurface.FolderBackground,
            ContextMenuSurface.DesktopBackground,
        };
        var tabs = ContextMenuTabMapper.GetTabs("Folder background", surfaces);
        Assert.Contains(ContextMenuTab.FolderBackground, tabs);
        Assert.Contains(ContextMenuTab.Desktop, tabs);
    }

    [Fact]
    public void GetTabs_folder_background_empty_probe_falls_back_to_both()
    {
        var surfaces = new HashSet<ContextMenuSurface>();
        var tabs = ContextMenuTabMapper.GetTabs("Folder background", surfaces);
        Assert.Contains(ContextMenuTab.FolderBackground, tabs);
        Assert.Contains(ContextMenuTab.Desktop, tabs);
    }

    [Fact]
    public void GetTabs_desktop_background_maps_to_desktop_only()
    {
        var tabs = ContextMenuTabMapper.GetTabs("Desktop background");
        Assert.Single(tabs);
        Assert.Contains(ContextMenuTab.Desktop, tabs);
    }

    [Theory]
    [InlineData("Drives")]
    [InlineData("This PC")]
    [InlineData("Network")]
    [InlineData("Recycle Bin")]
    public void GetTabs_misc_surfaces(string appliesTo)
    {
        var tabs = ContextMenuTabMapper.GetTabs(appliesTo);
        Assert.Contains(ContextMenuTab.Misc, tabs);
    }

    [Fact]
    public void GetMiscGroup_drives_returns_Drive()
    {
        Assert.Equal(MiscSurfaceGroup.Drive, ContextMenuTabMapper.GetMiscGroup("Drives"));
    }

    [Fact]
    public void GetMiscGroup_this_pc_returns_ThisPc()
    {
        Assert.Equal(MiscSurfaceGroup.ThisPc, ContextMenuTabMapper.GetMiscGroup("This PC"));
    }

    [Fact]
    public void GetMiscGroup_network_returns_Network()
    {
        Assert.Equal(MiscSurfaceGroup.Network, ContextMenuTabMapper.GetMiscGroup("Network"));
    }

    [Fact]
    public void GetMiscGroup_recycle_bin_returns_RecycleBin()
    {
        Assert.Equal(MiscSurfaceGroup.RecycleBin, ContextMenuTabMapper.GetMiscGroup("Recycle Bin"));
    }

    [Fact]
    public void GetMiscGroup_non_misc_surface_returns_null()
    {
        Assert.Null(ContextMenuTabMapper.GetMiscGroup("All files"));
    }

    // Static verb scope tab mapping tests

    [Fact]
    public void GetTabsForStaticVerbScope_drives_maps_to_Misc()
    {
        var tabs = ContextMenuTabMapper.GetTabsForStaticVerbScope("Drives");
        Assert.Contains(ContextMenuTab.Misc, tabs);
    }

    [Fact]
    public void GetTabsForStaticVerbScope_directory_background_maps_to_both_tabs()
    {
        var tabs = ContextMenuTabMapper.GetTabsForStaticVerbScope("Folder background");
        Assert.Contains(ContextMenuTab.FolderBackground, tabs);
        Assert.Contains(ContextMenuTab.Desktop, tabs);
    }

    [Fact]
    public void GetTabsForStaticVerbScope_desktop_background_maps_to_Desktop()
    {
        var tabs = ContextMenuTabMapper.GetTabsForStaticVerbScope("Desktop background");
        Assert.Contains(ContextMenuTab.Desktop, tabs);
    }

    [Fact]
    public void GetTabsForStaticVerbScope_all_files_maps_to_File()
    {
        var tabs = ContextMenuTabMapper.GetTabsForStaticVerbScope("All files");
        Assert.Contains(ContextMenuTab.File, tabs);
    }

    [Fact]
    public void GetTabsForStaticVerbScope_directories_maps_to_Folder()
    {
        var tabs = ContextMenuTabMapper.GetTabsForStaticVerbScope("Directories");
        Assert.Contains(ContextMenuTab.Folder, tabs);
    }

    [Fact]
    public void GetTabsForStaticVerbScope_folders_maps_to_Folder()
    {
        var tabs = ContextMenuTabMapper.GetTabsForStaticVerbScope("Folders");
        Assert.Contains(ContextMenuTab.Folder, tabs);
    }
}
