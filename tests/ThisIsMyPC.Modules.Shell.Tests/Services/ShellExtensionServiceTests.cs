using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

public class ShellExtensionServiceTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly ShellExtensionService _sut;

    public ShellExtensionServiceTests()
    {
        _sut = new ShellExtensionService(_registry);
    }

    [Fact]
    public void EnumerateContextMenuHandlers_returns_handler_from_star_path()
    {
        var basePath = @"HKCR\*\shellex\ContextMenuHandlers";
        _registry.AddSubKeys(basePath, "TestHandler");
        _registry.SetString($@"{basePath}\TestHandler", string.Empty, "{12345678-1234-1234-1234-123456789012}");

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("TestHandler", result.Value![0].HandlerName);
        Assert.Equal("{12345678-1234-1234-1234-123456789012}", result.Value![0].Clsid);
        Assert.Equal("All files", result.Value![0].AppliesTo);
        Assert.True(result.Value![0].IsEnabled);
    }

    [Fact]
    public void EnumerateContextMenuHandlers_detects_disabled_handler_with_dash_prefix()
    {
        var basePath = @"HKCR\Directory\shellex\ContextMenuHandlers";
        _registry.AddSubKeys(basePath, "DisabledHandler");
        _registry.SetString($@"{basePath}\DisabledHandler", string.Empty, "-{AABBCCDD-1234-5678-9012-ABCDEF012345}");

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.False(result.Value![0].IsEnabled);
        Assert.Equal("{AABBCCDD-1234-5678-9012-ABCDEF012345}", result.Value![0].Clsid);
        Assert.Equal("Directories", result.Value![0].AppliesTo);
    }

    [Fact]
    public void EnumerateContextMenuHandlers_returns_all_registrations_for_same_clsid_across_paths()
    {
        var clsid = "{11111111-2222-3333-4444-555555555555}";

        // Register same CLSID under two paths
        var path1 = @"HKCR\*\shellex\ContextMenuHandlers";
        _registry.AddSubKeys(path1, "Handler1");
        _registry.SetString($@"{path1}\Handler1", string.Empty, clsid);

        var path2 = @"HKCR\Directory\shellex\ContextMenuHandlers";
        _registry.AddSubKeys(path2, "Handler1");
        _registry.SetString($@"{path2}\Handler1", string.Empty, clsid);

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);

        var allFiles = result.Value!.First(h => h.AppliesTo == "All files");
        var directories = result.Value!.First(h => h.AppliesTo == "Directories");

        Assert.Equal(clsid, allFiles.Clsid);
        Assert.Equal(clsid, directories.Clsid);
        Assert.Contains(@"HKCR\*\", allFiles.RegistryPath);
        Assert.Contains(@"HKCR\Directory\", directories.RegistryPath);
    }

    [Fact]
    public void EnumerateContextMenuHandlers_returns_empty_when_no_handlers()
    {
        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public void EnumerateContextMenuHandlers_skips_handler_with_empty_clsid()
    {
        var basePath = @"HKCR\Folder\shellex\ContextMenuHandlers";
        _registry.AddSubKeys(basePath, "EmptyHandler", "ValidHandler");
        _registry.SetString($@"{basePath}\EmptyHandler", string.Empty, "");
        _registry.SetString($@"{basePath}\ValidHandler", string.Empty, "{99999999-8888-7777-6666-555544443333}");

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("ValidHandler", result.Value![0].HandlerName);
    }

    [Fact]
    public void EnumerateContextMenuHandlers_resolves_dll_path_from_inprocserver32()
    {
        var basePath = @"HKCR\*\shellex\ContextMenuHandlers";
        var clsid = "{DEADBEEF-CAFE-BABE-FEED-FACE12345678}";
        _registry.AddSubKeys(basePath, "DllHandler");
        _registry.SetString($@"{basePath}\DllHandler", string.Empty, clsid);
        _registry.SetString($@"HKCR\CLSID\{clsid}\InprocServer32", string.Empty, @"C:\Windows\System32\test.dll");

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(@"C:\Windows\System32\test.dll", result.Value![0].DllPath);
        Assert.Equal(clsid, result.Value![0].Clsid);
    }

    [Fact]
    public void EnumerateContextMenuHandlers_handles_multiple_paths()
    {
        var path1 = @"HKCR\*\shellex\ContextMenuHandlers";
        _registry.AddSubKeys(path1, "Handler1");
        _registry.SetString($@"{path1}\Handler1", string.Empty, "{11111111-1111-1111-1111-111111111111}");

        var path2 = @"HKCR\Directory\Background\shellex\ContextMenuHandlers";
        _registry.AddSubKeys(path2, "Handler2");
        _registry.SetString($@"{path2}\Handler2", string.Empty, "{22222222-2222-2222-2222-222222222222}");

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);

        var handler1 = result.Value!.First(h => h.HandlerName == "Handler1");
        var handler2 = result.Value!.First(h => h.HandlerName == "Handler2");

        Assert.Equal("All files", handler1.AppliesTo);
        Assert.Equal("Folder background", handler2.AppliesTo);
    }

    [Fact]
    public void EnumerateContextMenuHandlers_strips_dash_from_clsid_in_result()
    {
        var basePath = @"HKCR\*\shellex\ContextMenuHandlers";
        _registry.AddSubKeys(basePath, "DashedHandler");
        _registry.SetString($@"{basePath}\DashedHandler", string.Empty, "-{AAAABBBB-CCCC-DDDD-EEEE-FFFFFFFFFFFF}");

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        // Clsid should have dash stripped (clean CLSID)
        Assert.Equal("{AAAABBBB-CCCC-DDDD-EEEE-FFFFFFFFFFFF}", result.Value![0].Clsid);
        Assert.False(result.Value![0].IsEnabled);
    }
}
