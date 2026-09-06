using ThisIsMyPC.Interop.Com.Startup;

namespace ThisIsMyPC.Security.Tests;

[Trait("Category", "Security")]
public sealed class StartupFolderContainmentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tipc-startup-security-{Guid.NewGuid():N}");
    private readonly string _userFolder;
    private readonly string _allUsersFolder;
    private readonly StartupFolderService _service;

    public StartupFolderContainmentTests()
    {
        _userFolder = Path.Combine(_root, "user");
        _allUsersFolder = Path.Combine(_root, "all");
        Directory.CreateDirectory(_userFolder);
        Directory.CreateDirectory(_allUsersFolder);
        _service = new StartupFolderService(_userFolder, _allUsersFolder);
    }

    [Fact]
    public void Enumerate_DoesNotParseShortcutContents()
    {
        var shortcut = Path.Combine(_userFolder, "malformed.lnk");
        File.WriteAllBytes(shortcut, [0xff, 0x00, 0x13, 0x37]);

        var item = Assert.Single(_service.Enumerate(Core.Services.StartupFolderScope.CurrentUser).Value!);

        Assert.Equal(shortcut, item.FilePath);
        Assert.Null(item.ResolvedTarget);
        Assert.Equal([0xff, 0x00, 0x13, 0x37], File.ReadAllBytes(shortcut));
    }

    [Theory]
    [InlineData("outside.txt")]
    [InlineData("user\\..\\outside.txt")]
    [InlineData("user\\nested\\outside.txt")]
    public void FileOperations_RefusePathsOutsideManagedFolders(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "keep");

        Assert.False(_service.ReadAllBytes(path, 1024).IsSuccess);
        Assert.False(_service.Delete(path).IsSuccess);
        Assert.False(_service.Restore(path, [1, 2, 3]).IsSuccess);
        Assert.Equal("keep", File.ReadAllText(path));
    }

    [Fact]
    public void Move_RefusesCrossScopeAndOutsideDestinations()
    {
        var source = Path.Combine(_userFolder, "tool.lnk");
        File.WriteAllText(source, "keep");

        Assert.False(_service.Move(source, Path.Combine(_allUsersFolder, "tool.lnk")).IsSuccess);
        Assert.False(_service.Move(source, Path.Combine(_root, "tool.lnk")).IsSuccess);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void ManagedMoveAndRestoreStayInsideOneStartupFolder()
    {
        var source = Path.Combine(_userFolder, "tool.lnk");
        var parked = Path.Combine(_userFolder, Core.Services.IStartupFolderService.DisabledSubfolder, "tool.lnk");
        File.WriteAllText(source, "original");

        Assert.True(_service.Move(source, parked).IsSuccess);
        Assert.False(File.Exists(source));
        Assert.Equal("original", File.ReadAllText(parked));

        Assert.True(_service.Delete(parked).IsSuccess);
        Assert.True(_service.Restore(source, "restored"u8.ToArray()).IsSuccess);
        Assert.Equal("restored", File.ReadAllText(source));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
