using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

public sealed class FileTypeVerbServiceTests
{
    private readonly FakeRegistryService _registry = new();

    private void SetupVerb(string progIdPath, string verbName, string? command = null, string? muiVerb = null)
    {
        var verbPath = $@"{progIdPath}\shell\{verbName}";
        _registry.AddKey(verbPath);
        if (muiVerb is not null)
            _registry.SetString(verbPath, "MUIVerb", muiVerb);
        if (command is not null)
        {
            var cmdPath = $@"{verbPath}\command";
            _registry.AddKey(cmdPath);
            _registry.SetString(cmdPath, string.Empty, command);
        }
    }

    private void SetupComHandler(string progIdPath, string handlerName, string clsid)
    {
        var path = $@"{progIdPath}\shellex\ContextMenuHandlers\{handlerName}";
        _registry.AddKey(path);
        _registry.SetString(path, string.Empty, clsid);
    }

    [Fact]
    public void ScanVerbs_discovers_verb_at_ProgID()
    {
        SetupVerb(@"HKCR\pngfile", "edit", command: "mspaint.exe %1", muiVerb: "Edit with Paint");

        var service = new FileTypeVerbService(_registry);
        var entries = new[] { new ProgIdEntry(@"HKCR\pngfile", "pngfile", ProgIdSource.DefaultProgId) };
        var result = service.ScanVerbs(entries);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("edit", result.Value![0].VerbName);
        Assert.Equal("Edit with Paint", result.Value![0].MuiVerb);
    }

    [Fact]
    public void ScanVerbs_deduplicates_across_ProgID_levels()
    {
        SetupVerb(@"HKCR\pngfile", "print", command: "rundll32.exe print %1");
        SetupVerb(@"HKCR\SystemFileAssociations\image", "print", command: "rundll32.exe print %1");

        var service = new FileTypeVerbService(_registry);
        var entries = new[]
        {
            new ProgIdEntry(@"HKCR\pngfile", "pngfile", ProgIdSource.DefaultProgId),
            new ProgIdEntry(@"HKCR\SystemFileAssociations\image", "SFA\\image", ProgIdSource.PerceivedType),
        };
        var result = service.ScanVerbs(entries);

        Assert.True(result.IsSuccess);
        // Same verb name + command → deduplicated to one entry
        Assert.Single(result.Value!);
    }

    [Fact]
    public void ScanVerbs_keeps_different_verbs_with_same_name_different_command()
    {
        SetupVerb(@"HKCR\pngfile", "edit", command: "mspaint.exe %1");
        SetupVerb(@"HKCR\AppXabc", "edit", command: "photos.exe %1");

        var service = new FileTypeVerbService(_registry);
        var entries = new[]
        {
            new ProgIdEntry(@"HKCR\pngfile", "pngfile", ProgIdSource.DefaultProgId),
            new ProgIdEntry(@"HKCR\AppXabc", "AppXabc", ProgIdSource.OpenWithProgids),
        };
        var result = service.ScanVerbs(entries);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public void ScanVerbs_skips_ShellNew()
    {
        _registry.AddKey(@"HKCR\pngfile\shell");
        _registry.AddSubKeys(@"HKCR\pngfile\shell", "ShellNew", "edit");
        SetupVerb(@"HKCR\pngfile", "edit", command: "mspaint.exe %1");

        var service = new FileTypeVerbService(_registry);
        var entries = new[] { new ProgIdEntry(@"HKCR\pngfile", "pngfile", ProgIdSource.DefaultProgId) };
        var result = service.ScanVerbs(entries);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("edit", result.Value![0].VerbName);
    }

    [Fact]
    public void ScanComHandlers_discovers_handler()
    {
        SetupComHandler(@"HKCR\SystemFileAssociations\.png", "ImageResizer",
            "{51B4D7E5-7568-4234-B4BB-47FB3C016A69}");
        // Set up CLSID display name
        _registry.AddKey(@"HKCR\CLSID\{51B4D7E5-7568-4234-B4BB-47FB3C016A69}");
        _registry.SetString(@"HKCR\CLSID\{51B4D7E5-7568-4234-B4BB-47FB3C016A69}", string.Empty, "Image Resizer");

        var service = new FileTypeVerbService(_registry);
        var entries = new[] { new ProgIdEntry(@"HKCR\SystemFileAssociations\.png", "SFA\\.png", ProgIdSource.SystemFileAssociations) };
        var result = service.ScanComHandlers(entries);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("{51B4D7E5-7568-4234-B4BB-47FB3C016A69}", result.Value![0].Clsid);
    }

    [Fact]
    public void ScanComHandlers_deduplicates_by_CLSID()
    {
        SetupComHandler(@"HKCR\SystemFileAssociations\.png", "PlayTo",
            "{7AD84985-87B4-4a16-BE58-8B72A5B390F7}");
        SetupComHandler(@"HKCR\SystemFileAssociations\image", "PlayTo",
            "{7AD84985-87B4-4a16-BE58-8B72A5B390F7}");

        var service = new FileTypeVerbService(_registry);
        var entries = new[]
        {
            new ProgIdEntry(@"HKCR\SystemFileAssociations\.png", "SFA\\.png", ProgIdSource.SystemFileAssociations),
            new ProgIdEntry(@"HKCR\SystemFileAssociations\image", "SFA\\image", ProgIdSource.PerceivedType),
        };
        var result = service.ScanComHandlers(entries);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }

    [Fact]
    public void ScanComHandlers_detects_dash_prefixed_as_disabled()
    {
        var path = @"HKCR\exefile\shellex\ContextMenuHandlers\TestHandler";
        _registry.AddKey(path);
        _registry.SetString(path, string.Empty, "-{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}");

        var service = new FileTypeVerbService(_registry);
        var entries = new[] { new ProgIdEntry(@"HKCR\exefile", "exefile", ProgIdSource.DefaultProgId) };
        var result = service.ScanComHandlers(entries);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.False(result.Value![0].IsEnabled);
        Assert.Equal("{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}", result.Value![0].Clsid);
    }
}
