using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;
using ThisIsMyPC.Modules.Startup.Services;
using ThisIsMyPC.Modules.Startup.Tests.Fakes;

namespace ThisIsMyPC.Modules.Startup.Tests;

public class StartupModuleTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly FakeStartupFolderService _folders = new();

    private StartupModule CreateModule() => new(_registry, _folders);

    private static StartupEntry MakeEntry() => new()
    {
        Name = "App",
        Command = @"C:\app.exe",
        Source = StartupSource.RegistryUserRun,
        SourceLocation = StartupScanner.UserRunKey,
        IsEnabled = true,
    };

    [Fact]
    public async Task ApplyChange_WritesDisabledBlob()
    {
        var change = StartupChangeFactory.CreateToggle(MakeEntry(), enable: false, currentApprovedBlob: null)!;

        var result = await CreateModule().ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        var written = _registry.ReadBinary(StartupScanner.UserApprovedRunKey, "App");
        Assert.True(written.IsSuccess);
        Assert.Equal(StartupChangeFactory.DisabledBlob, written.Value);
    }

    [Fact]
    public async Task RevertChange_AbsentBeforeValue_DeletesTheValue()
    {
        var change = StartupChangeFactory.CreateToggle(MakeEntry(), enable: false, currentApprovedBlob: null)!;
        var module = CreateModule();
        await module.ApplyChangeAsync(change);

        // Revert contract: Before/After swapped descriptor
        var reverted = change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue };
        var result = await module.RevertChangeAsync(reverted);

        Assert.True(result.IsSuccess);
        Assert.False(_registry.ValueExists(StartupScanner.UserApprovedRunKey, "App").Value);
    }

    [Fact]
    public async Task RevertChange_ExistingBeforeBlob_RestoresIt()
    {
        var original = new byte[] { 0x02, 0, 0, 0, 0xAA, 0xBB, 0, 0, 0, 0, 0, 0 };
        _registry.SetBinary(StartupScanner.UserApprovedRunKey, "App", original);
        var change = StartupChangeFactory.CreateToggle(MakeEntry(), enable: false, currentApprovedBlob: original)!;
        var module = CreateModule();
        await module.ApplyChangeAsync(change);

        var reverted = change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue };
        var result = await module.RevertChangeAsync(reverted);

        Assert.True(result.IsSuccess);
        Assert.Equal(original, _registry.ReadBinary(StartupScanner.UserApprovedRunKey, "App").Value);
    }

    [Fact]
    public async Task ApplyChange_UnsupportedValueType_Fails()
    {
        var change = StartupChangeFactory.CreateToggle(MakeEntry(), enable: false, currentApprovedBlob: null)!
            with { ValueType = ChangeValueType.Service_StartType };

        var result = await CreateModule().ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
        Assert.Contains("Unsupported", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyChange_MalformedHex_Fails()
    {
        var change = StartupChangeFactory.CreateToggle(MakeEntry(), enable: false, currentApprovedBlob: null)!
            with { AfterValue = "not-hex" };

        var result = await CreateModule().ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ScanSystemState_ReturnsStartupScanData()
    {
        _registry.SetString(StartupScanner.UserRunKey, "App", @"C:\app.exe");

        var result = await CreateModule().ScanSystemStateAsync();

        Assert.True(result.IsSuccess);
        var data = Assert.IsType<StartupScanData>(result.Value);
        Assert.Single(data.StartupEntries);
    }
}
