using ThisIsMyPC.Interop.Win32.Registry;

namespace ThisIsMyPC.Integration.Tests.Registry;

[Trait("Category", "Integration")]
public sealed class ExplorerPreferencesIntegrationTests : IDisposable
{
    private const string SandboxKeyPath = @"HKCU\Software\ThisIsMyPC\TestsExplorerPrefs";
    private const string SandboxAdvancedKeyPath = @"HKCU\Software\ThisIsMyPC\TestsExplorerPrefs\Advanced";
    private readonly RegistryService _sut = new();

    public ExplorerPreferencesIntegrationTests()
    {
        // Ensure sandbox key exists
        _sut.WriteDWord(SandboxAdvancedKeyPath, "setup", 1);
    }

    public void Dispose()
    {
        _sut.DeleteKey(SandboxKeyPath, recursive: true);
    }

    [Fact]
    public void NavPaneShowAllFolders_DWord_write_readback()
    {
        var writeResult = _sut.WriteDWord(SandboxAdvancedKeyPath, "NavPaneShowAllFolders", 1);
        Assert.True(writeResult.IsSuccess);

        var readResult = _sut.ReadDWord(SandboxAdvancedKeyPath, "NavPaneShowAllFolders");
        Assert.True(readResult.IsSuccess);
        Assert.Equal(1, readResult.Value);

        // Toggle off
        var writeOff = _sut.WriteDWord(SandboxAdvancedKeyPath, "NavPaneShowAllFolders", 0);
        Assert.True(writeOff.IsSuccess);

        var readOff = _sut.ReadDWord(SandboxAdvancedKeyPath, "NavPaneShowAllFolders");
        Assert.True(readOff.IsSuccess);
        Assert.Equal(0, readOff.Value);
    }

    [Fact]
    public void NavPaneExpandToCurrentFolder_DWord_write_readback()
    {
        var writeResult = _sut.WriteDWord(SandboxAdvancedKeyPath, "NavPaneExpandToCurrentFolder", 1);
        Assert.True(writeResult.IsSuccess);

        var readResult = _sut.ReadDWord(SandboxAdvancedKeyPath, "NavPaneExpandToCurrentFolder");
        Assert.True(readResult.IsSuccess);
        Assert.Equal(1, readResult.Value);

        // Toggle off
        var writeOff = _sut.WriteDWord(SandboxAdvancedKeyPath, "NavPaneExpandToCurrentFolder", 0);
        Assert.True(writeOff.IsSuccess);

        var readOff = _sut.ReadDWord(SandboxAdvancedKeyPath, "NavPaneExpandToCurrentFolder");
        Assert.True(readOff.IsSuccess);
        Assert.Equal(0, readOff.Value);
    }

    [Fact]
    public void UseCompactMode_DWord_write_readback()
    {
        var writeResult = _sut.WriteDWord(SandboxAdvancedKeyPath, "UseCompactMode", 1);
        Assert.True(writeResult.IsSuccess);

        var readResult = _sut.ReadDWord(SandboxAdvancedKeyPath, "UseCompactMode");
        Assert.True(readResult.IsSuccess);
        Assert.Equal(1, readResult.Value);

        // Toggle off
        var writeOff = _sut.WriteDWord(SandboxAdvancedKeyPath, "UseCompactMode", 0);
        Assert.True(writeOff.IsSuccess);

        var readOff = _sut.ReadDWord(SandboxAdvancedKeyPath, "UseCompactMode");
        Assert.True(readOff.IsSuccess);
        Assert.Equal(0, readOff.Value);
    }
}
