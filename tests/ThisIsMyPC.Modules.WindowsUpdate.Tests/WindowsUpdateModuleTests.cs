using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.WindowsUpdate;
using ThisIsMyPC.Modules.WindowsUpdate.Changes;
using ThisIsMyPC.Modules.WindowsUpdate.Models;
using ThisIsMyPC.Modules.WindowsUpdate.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Tests.Fakes;

namespace ThisIsMyPC.Modules.WindowsUpdate.Tests;

public class WindowsUpdateModuleTests
{
    private static UpdatePolicySetting Setting(string current = "") => new(
        Id: "no-auto-reboot",
        DisplayName: "Never auto-restart while you are signed in",
        Description: "d",
        RegistryKeyPath: WindowsUpdateRegistryPaths.AuPoliciesKeyPath,
        RegistryValueName: "NoAutoRebootWithLoggedOnUsers",
        ValueType: ChangeValueType.Registry_DWord,
        CurrentValue: current,
        ConfiguredValue: "1");

    [Fact]
    public async Task ApplyChange_Configure_WritesDWord()
    {
        var registry = new FakeRegistryService();
        var module = new WindowsUpdateModule(registry);
        var change = WindowsUpdateChangeFactory.CreateToggle(Setting(), configure: true);

        var result = await module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        var read = registry.ReadDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoRebootWithLoggedOnUsers");
        Assert.True(read.IsSuccess);
        Assert.Equal(1, read.Value);
    }

    [Fact]
    public async Task ApplyChange_EmptyAfterValue_DeletesTheValue()
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoRebootWithLoggedOnUsers", 1);
        var module = new WindowsUpdateModule(registry);
        var change = WindowsUpdateChangeFactory.CreateToggle(Setting(current: "1"), configure: false);

        var result = await module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        Assert.False(registry.ReadDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoRebootWithLoggedOnUsers").IsSuccess);
    }

    [Fact]
    public async Task ApplyChange_StringPolicy_WritesString()
    {
        var registry = new FakeRegistryService();
        registry.SetString(WindowsUpdateRegistryPaths.CurrentVersionKeyPath, "DisplayVersion", "24H2");
        var module = new WindowsUpdateModule(registry);
        var pin = new WindowsUpdateSettingsReader(registry).ReadVersionPin();
        var change = WindowsUpdateChangeFactory.CreateToggle(pin[2], configure: true);

        var result = await module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        var read = registry.ReadString(WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath, "TargetReleaseVersionInfo");
        Assert.True(read.IsSuccess);
        Assert.Equal("24H2", read.Value);
    }

    [Fact]
    public async Task Scan_ReturnsScanData()
    {
        var module = new WindowsUpdateModule(new FakeRegistryService());

        var result = await module.ScanSystemStateAsync();

        Assert.True(result.IsSuccess);
        var data = Assert.IsType<WindowsUpdateScanData>(result.Value);
        Assert.Equal(4, data.Settings.Count);
        Assert.Empty(data.VersionPin); // no DisplayVersion in the fake
    }
}
