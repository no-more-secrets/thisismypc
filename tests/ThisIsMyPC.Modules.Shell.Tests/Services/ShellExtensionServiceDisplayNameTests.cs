using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

public class ShellExtensionServiceDisplayNameTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly ShellExtensionService _sut;

    public ShellExtensionServiceDisplayNameTests()
    {
        _sut = new ShellExtensionService(_registry);
    }

    [Fact]
    public void CLSID_default_value_used_as_display_name_when_friendly()
    {
        var basePath = @"HKCR\*\shellex\ContextMenuHandlers";
        var clsid = "{00000000-1111-2222-3333-444444444444}";
        _registry.AddSubKeys(basePath, "FileLocksmithExt");
        _registry.SetString($@"{basePath}\FileLocksmithExt", string.Empty, clsid);
        // CLSID key has a friendly default value
        _registry.SetString($@"HKCR\CLSID\{clsid}", string.Empty, "File Locksmith");

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("File Locksmith", result.Value![0].HandlerName);
        Assert.Equal("FileLocksmithExt", result.Value![0].RegistryKeyName);
    }

    [Fact]
    public void CLSID_default_value_skipped_when_indirect_string()
    {
        var basePath = @"HKCR\*\shellex\ContextMenuHandlers";
        var clsid = "{00000000-1111-2222-3333-555555555555}";
        _registry.AddSubKeys(basePath, "SharingMenu");
        _registry.SetString($@"{basePath}\SharingMenu", string.Empty, clsid);
        // CLSID key has indirect string resource — skip it
        _registry.SetString($@"HKCR\CLSID\{clsid}", string.Empty, "@shell32.dll,-51608");

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("SharingMenu", result.Value![0].HandlerName);
    }

    [Fact]
    public void CLSID_default_value_skipped_when_no_default_value()
    {
        var basePath = @"HKCR\*\shellex\ContextMenuHandlers";
        var clsid = "{00000000-1111-2222-3333-666666666666}";
        _registry.AddSubKeys(basePath, "SimpleHandler");
        _registry.SetString($@"{basePath}\SimpleHandler", string.Empty, clsid);
        // No CLSID\{guid} default value set at all

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("SimpleHandler", result.Value![0].HandlerName);
    }

    [Fact]
    public void CLSID_default_value_skipped_when_looks_like_clsid()
    {
        var basePath = @"HKCR\*\shellex\ContextMenuHandlers";
        var clsid = "{00000000-1111-2222-3333-777777777777}";
        _registry.AddSubKeys(basePath, "LoopbackHandler");
        _registry.SetString($@"{basePath}\LoopbackHandler", string.Empty, clsid);
        // CLSID key default value is itself a CLSID — skip it
        _registry.SetString($@"HKCR\CLSID\{clsid}", string.Empty, "{00000000-1111-2222-3333-777777777777}");

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("LoopbackHandler", result.Value![0].HandlerName);
    }

    [Fact]
    public void CLSID_default_value_skipped_when_empty()
    {
        var basePath = @"HKCR\*\shellex\ContextMenuHandlers";
        var clsid = "{00000000-1111-2222-3333-888888888888}";
        _registry.AddSubKeys(basePath, "EmptyNameHandler");
        _registry.SetString($@"{basePath}\EmptyNameHandler", string.Empty, clsid);
        // CLSID key has empty default value
        _registry.SetString($@"HKCR\CLSID\{clsid}", string.Empty, "");

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("EmptyNameHandler", result.Value![0].HandlerName);
    }

    [Fact]
    public void RegistryKeyName_preserved_when_CLSID_display_name_used()
    {
        var basePath = @"HKCR\*\shellex\ContextMenuHandlers";
        var clsid = "{00000000-1111-2222-3333-999999999999}";
        _registry.AddSubKeys(basePath, "CopyAsPathMenu");
        _registry.SetString($@"{basePath}\CopyAsPathMenu", string.Empty, clsid);
        _registry.SetString($@"HKCR\CLSID\{clsid}", string.Empty, "Copy As Path");

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("Copy As Path", result.Value![0].HandlerName);
        Assert.Equal("CopyAsPathMenu", result.Value![0].RegistryKeyName);
    }

    [Fact]
    public void RegistryKeyName_equals_HandlerName_when_no_CLSID_display_name()
    {
        var basePath = @"HKCR\*\shellex\ContextMenuHandlers";
        var clsid = "{00000000-1111-2222-3333-AAAAAAAAAAAA}";
        _registry.AddSubKeys(basePath, "PlainHandler");
        _registry.SetString($@"{basePath}\PlainHandler", string.Empty, clsid);
        // No CLSID default value

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("PlainHandler", result.Value![0].HandlerName);
        Assert.Equal("PlainHandler", result.Value![0].RegistryKeyName);
    }

    [Fact]
    public void CLSID_display_name_shared_across_multi_registration()
    {
        var clsid = "{00000000-1111-2222-3333-BBBBBBBBBBBB}";
        _registry.SetString($@"HKCR\CLSID\{clsid}", string.Empty, "7-Zip Shell Extension");

        var path1 = @"HKCR\*\shellex\ContextMenuHandlers";
        _registry.AddSubKeys(path1, "7-Zip");
        _registry.SetString($@"{path1}\7-Zip", string.Empty, clsid);

        var path2 = @"HKCR\Directory\shellex\ContextMenuHandlers";
        _registry.AddSubKeys(path2, "7-Zip");
        _registry.SetString($@"{path2}\7-Zip", string.Empty, clsid);

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.All(result.Value!, h => Assert.Equal("7-Zip Shell Extension", h.HandlerName));
        Assert.All(result.Value!, h => Assert.Equal("7-Zip", h.RegistryKeyName));
    }

    [Fact]
    public void Audio_folder_scope_scanned_from_SystemFileAssociations()
    {
        var basePath = @"HKCR\SystemFileAssociations\Directory.Audio\shellex\ContextMenuHandlers";
        var clsid = "{00000000-1111-2222-3333-CCCCCCCCCCCC}";
        _registry.AddSubKeys(basePath, "WMPShellExt");
        _registry.SetString($@"{basePath}\WMPShellExt", string.Empty, clsid);

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("Audio folders", result.Value![0].AppliesTo);
    }

    [Fact]
    public void Video_folder_scope_scanned_from_SystemFileAssociations()
    {
        var basePath = @"HKCR\SystemFileAssociations\Directory.Video\shellex\ContextMenuHandlers";
        var clsid = "{00000000-1111-2222-3333-DDDDDDDDDDDD}";
        _registry.AddSubKeys(basePath, "VideoHandler");
        _registry.SetString($@"{basePath}\VideoHandler", string.Empty, clsid);

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("Video folders", result.Value![0].AppliesTo);
    }

    [Fact]
    public void Inverted_CLSID_registration_still_resolves_display_name()
    {
        var basePath = @"HKCR\*\shellex\ContextMenuHandlers";
        var clsid = "{AAAABBBB-CCCC-DDDD-EEEE-FFFFFFFFFFFF}";
        // Inverted: key name is the CLSID, default value is the friendly name
        _registry.AddSubKeys(basePath, clsid);
        _registry.SetString($@"{basePath}\{clsid}", string.Empty, "Taskband Pin");
        // CLSID key also has a friendly display name
        _registry.SetString($@"HKCR\CLSID\{clsid}", string.Empty, "Taskbar Pin Handler");

        var result = _sut.EnumerateContextMenuHandlers();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        // After inverted detection: resolvedName = "Taskband Pin", cleanClsid = "{AAAABBBB-...}"
        // Then CLSID display name resolution: CLSID key has "Taskbar Pin Handler" → use it
        Assert.Equal("Taskbar Pin Handler", result.Value![0].HandlerName);
        Assert.Equal("Taskband Pin", result.Value![0].RegistryKeyName);
    }
}
