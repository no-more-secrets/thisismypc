using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

public sealed class ProgIdResolverTests
{
    private readonly FakeRegistryService _registry = new();

    [Fact]
    public void Resolve_simple_chain_returns_DefaultProgId()
    {
        _registry.AddKey(@"HKCR\.txt");
        _registry.SetString(@"HKCR\.txt", string.Empty, "txtfile");

        var resolver = new ProgIdResolver(_registry);
        var result = resolver.Resolve(".txt");

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!, e => e.ProgId == "txtfile" && e.Source == ProgIdSource.DefaultProgId);
    }

    [Fact]
    public void Resolve_includes_PerceivedType()
    {
        _registry.AddKey(@"HKCR\.png");
        _registry.SetString(@"HKCR\.png", string.Empty, "pngfile");
        _registry.SetString(@"HKCR\.png", "PerceivedType", "image");

        var resolver = new ProgIdResolver(_registry);
        var result = resolver.Resolve(".png");

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!, e => e.Source == ProgIdSource.PerceivedType
            && e.KeyPath == @"HKCR\SystemFileAssociations\image");
    }

    [Fact]
    public void Resolve_includes_OpenWithProgids()
    {
        _registry.AddKey(@"HKCR\.png");
        _registry.SetString(@"HKCR\.png", string.Empty, "pngfile");
        _registry.AddKey(@"HKCR\.png\OpenWithProgids");
        _registry.SetString(@"HKCR\.png\OpenWithProgids", "AppXabc123", string.Empty);

        var resolver = new ProgIdResolver(_registry);
        var result = resolver.Resolve(".png");

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!, e => e.ProgId == "AppXabc123" && e.Source == ProgIdSource.OpenWithProgids);
    }

    [Fact]
    public void Resolve_deduplicates_ProgIds_across_DefaultProgId_and_OpenWithProgids()
    {
        _registry.AddKey(@"HKCR\.png");
        _registry.SetString(@"HKCR\.png", string.Empty, "pngfile");
        _registry.AddKey(@"HKCR\.png\OpenWithProgids");
        _registry.SetString(@"HKCR\.png\OpenWithProgids", "pngfile", string.Empty);
        _registry.SetString(@"HKCR\.png\OpenWithProgids", "otherApp", string.Empty);

        var resolver = new ProgIdResolver(_registry);
        var result = resolver.Resolve(".png");

        Assert.True(result.IsSuccess);
        // pngfile should appear only once (from DefaultProgId)
        var pngEntries = result.Value!.Where(e => e.ProgId.Equals("pngfile", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(pngEntries);
        Assert.Equal(ProgIdSource.DefaultProgId, pngEntries[0].Source);
    }

    [Fact]
    public void Resolve_always_includes_SystemFileAssociations_per_extension()
    {
        _registry.AddKey(@"HKCR\.xyz");

        var resolver = new ProgIdResolver(_registry);
        var result = resolver.Resolve(".xyz");

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!, e => e.Source == ProgIdSource.SystemFileAssociations
            && e.KeyPath == @"HKCR\SystemFileAssociations\.xyz");
    }

    [Fact]
    public void Resolve_missing_extension_returns_SFA_only()
    {
        // Extension key doesn't exist — ReadString will fail, but SFA is always added
        var resolver = new ProgIdResolver(_registry);
        var result = resolver.Resolve(".nonexistent");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!); // Only SystemFileAssociations
        Assert.Equal(ProgIdSource.SystemFileAssociations, result.Value![0].Source);
    }

    [Fact]
    public void Resolve_invalid_extension_returns_failure()
    {
        var resolver = new ProgIdResolver(_registry);
        var result = resolver.Resolve("noperiod");

        Assert.False(result.IsSuccess);
    }
}
