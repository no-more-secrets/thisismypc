using ThisIsMyPC.App.ViewModels;

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
    public void GetTabs_folder_background_maps_to_both_tabs()
    {
        var tabs = ContextMenuTabMapper.GetTabs("Folder background");
        Assert.Contains(ContextMenuTab.FolderBackground, tabs);
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
}
