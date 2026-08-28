using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;
using ThisIsMyPC.Modules.Startup.Services;
using ThisIsMyPC.Modules.Startup.Tests.Fakes;

namespace ThisIsMyPC.Modules.Startup.Tests.Services;

public class StartupScannerTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly FakeStartupFolderService _folders = new();

    private StartupScanner CreateScanner(Func<string, StartupFileMetadata>? metadataReader = null)
        => new(_registry, _folders, metadataReader ?? (_ => new StartupFileMetadata(null, null)));

    [Fact]
    public void Scan_DiscoversEntriesFromAllThreeRunKeys()
    {
        _registry.SetString(StartupScanner.MachineRunKey, "MachineApp", @"C:\Tools\machine.exe");
        _registry.SetString(StartupScanner.MachineRunWow64Key, "Wow64App", @"C:\Tools\wow64.exe");
        _registry.SetString(StartupScanner.UserRunKey, "UserApp", @"C:\Tools\user.exe");

        var data = CreateScanner().Scan();

        Assert.Equal(3, data.StartupEntries.Count);
        Assert.Contains(data.StartupEntries, e => e.Name == "MachineApp" && e.Source == StartupSource.RegistryMachineRun && e.SourceLocation == StartupScanner.MachineRunKey);
        Assert.Contains(data.StartupEntries, e => e.Name == "Wow64App" && e.Source == StartupSource.RegistryMachineRunWow64);
        Assert.Contains(data.StartupEntries, e => e.Name == "UserApp" && e.Source == StartupSource.RegistryUserRun);
    }

    [Fact]
    public void Scan_MissingRunKeys_ProducesEmptyResult()
    {
        var data = CreateScanner().Scan();
        Assert.Empty(data.StartupEntries);
    }

    [Fact]
    public void Scan_EntryWithoutApprovedState_IsEnabled()
    {
        _registry.SetString(StartupScanner.UserRunKey, "App", @"C:\app.exe");

        var data = CreateScanner().Scan();

        Assert.True(Assert.Single(data.StartupEntries).IsEnabled);
    }

    [Theory]
    [InlineData(0x02, true)]
    [InlineData(0x06, true)]
    [InlineData(0x03, false)]
    [InlineData(0x07, false)]
    public void Scan_ApprovedStateFirstByte_DeterminesEnabledState(byte firstByte, bool expectedEnabled)
    {
        _registry.SetString(StartupScanner.UserRunKey, "App", @"C:\app.exe");
        _registry.SetBinary(StartupScanner.UserApprovedRunKey, "App",
            [firstByte, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        var data = CreateScanner().Scan();

        Assert.Equal(expectedEnabled, Assert.Single(data.StartupEntries).IsEnabled);
    }

    [Fact]
    public void Scan_EmptyApprovedBlob_TreatedAsEnabled()
    {
        _registry.SetString(StartupScanner.UserRunKey, "App", @"C:\app.exe");
        _registry.SetBinary(StartupScanner.UserApprovedRunKey, "App", []);

        var data = CreateScanner().Scan();

        Assert.True(Assert.Single(data.StartupEntries).IsEnabled);
    }

    [Fact]
    public void Scan_MachineEntries_UseMachineApprovedKeys()
    {
        _registry.SetString(StartupScanner.MachineRunKey, "App", @"C:\app.exe");
        _registry.SetString(StartupScanner.MachineRunWow64Key, "App32", @"C:\app32.exe");
        _registry.SetBinary(StartupScanner.MachineApprovedRunKey, "App", [0x03, 0, 0, 0]);
        _registry.SetBinary(StartupScanner.MachineApprovedRun32Key, "App32", [0x03, 0, 0, 0]);

        var data = CreateScanner().Scan();

        Assert.All(data.StartupEntries, e => Assert.False(e.IsEnabled));
    }

    [Fact]
    public void Scan_SkipsDefaultValueAndBlankCommands()
    {
        _registry.SetString(StartupScanner.UserRunKey, "", @"C:\default.exe");
        _registry.SetString(StartupScanner.UserRunKey, "Blank", "   ");
        _registry.SetString(StartupScanner.UserRunKey, "Real", @"C:\real.exe");

        var data = CreateScanner().Scan();

        Assert.Equal("Real", Assert.Single(data.StartupEntries).Name);
    }

    [Fact]
    public void Scan_StartupFolderItems_AppearWithResolvedTargets()
    {
        _folders.AddItem(StartupFolderScope.CurrentUser, @"C:\Users\x\Startup\Tool.lnk", @"C:\Program Files\Tool\tool.exe");
        _folders.AddItem(StartupFolderScope.AllUsers, @"C:\ProgramData\Startup\Common.exe");

        var data = CreateScanner().Scan();

        Assert.Equal(2, data.StartupEntries.Count);
        var userEntry = data.StartupEntries.Single(e => e.Source == StartupSource.StartupFolderUser);
        Assert.Equal("Tool.lnk", userEntry.Name);
        Assert.Equal(@"C:\Program Files\Tool\tool.exe", userEntry.ExecutablePath);
        Assert.Equal(@"C:\Users\x\Startup", userEntry.SourceLocation);

        var commonEntry = data.StartupEntries.Single(e => e.Source == StartupSource.StartupFolderCommon);
        Assert.Equal(@"C:\ProgramData\Startup\Common.exe", commonEntry.ExecutablePath); // bare .exe is its own target
    }

    [Fact]
    public void Scan_UnresolvedShortcut_HasNullExecutablePath()
    {
        _folders.AddItem(StartupFolderScope.CurrentUser, @"C:\Users\x\Startup\Broken.lnk");

        var data = CreateScanner().Scan();

        var entry = Assert.Single(data.StartupEntries);
        Assert.Null(entry.ExecutablePath);
        Assert.Equal(@"C:\Users\x\Startup\Broken.lnk", entry.Command);
    }

    [Fact]
    public void Scan_FolderEntryDisabledViaStartupApproved()
    {
        _folders.AddItem(StartupFolderScope.CurrentUser, @"C:\Users\x\Startup\Tool.lnk", @"C:\tool.exe");
        _registry.SetBinary(StartupScanner.UserApprovedStartupFolderKey, "Tool.lnk", [0x03, 0, 0, 0]);

        var data = CreateScanner().Scan();

        Assert.False(Assert.Single(data.StartupEntries).IsEnabled);
    }

    [Fact]
    public void Scan_FolderFailure_DoesNotAbortOtherSources()
    {
        _folders.InjectFailure(StartupFolderScope.CurrentUser);
        _registry.SetString(StartupScanner.UserRunKey, "App", @"C:\app.exe");

        var data = CreateScanner().Scan();

        Assert.Single(data.StartupEntries);
    }

    [Fact]
    public void Scan_PublisherAndDescription_ComeFromMetadataReader()
    {
        _registry.SetString(StartupScanner.UserRunKey, "App", @"C:\app.exe");

        var data = CreateScanner(path => path.Equals(@"C:\app.exe", StringComparison.OrdinalIgnoreCase)
            ? new StartupFileMetadata("Contoso", "Contoso App")
            : new StartupFileMetadata(null, null)).Scan();

        var entry = Assert.Single(data.StartupEntries);
        Assert.Equal("Contoso", entry.Publisher);
        Assert.Equal("Contoso App", entry.Description);
    }

    [Fact]
    public void Scan_MetadataReader_IsCachedPerPath()
    {
        _registry.SetString(StartupScanner.MachineRunKey, "A", @"C:\same.exe");
        _registry.SetString(StartupScanner.UserRunKey, "B", @"C:\same.exe");

        var calls = 0;
        CreateScanner(_ => { calls++; return new StartupFileMetadata(null, null); }).Scan();

        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(@"C:\Tools\app.exe", @"C:\Tools\app.exe")]
    [InlineData(@"""C:\Program Files\App\app.exe"" /tray", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Tools\app.exe -minimized", @"C:\Tools\app.exe")]
    [InlineData(@"C:\Program Files\App\app.exe -tray", @"C:\Program Files\App\app.exe")]
    [InlineData(@"rundll32.exe shell32.dll,Control_RunDLL", @"rundll32.exe")]
    public void ExtractExecutablePath_ParsesCommonCommandShapes(string command, string expected)
    {
        Assert.Equal(expected, StartupScanner.ExtractExecutablePath(command));
    }

    [Fact]
    public void ExtractExecutablePath_EmptyOrUnclosedQuote_ReturnsNull()
    {
        Assert.Null(StartupScanner.ExtractExecutablePath("   "));
        Assert.Null(StartupScanner.ExtractExecutablePath("\"\""));
    }
}
