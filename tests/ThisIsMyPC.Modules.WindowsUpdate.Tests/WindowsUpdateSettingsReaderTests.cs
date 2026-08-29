using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.WindowsUpdate;
using ThisIsMyPC.Modules.WindowsUpdate.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Tests.Fakes;

namespace ThisIsMyPC.Modules.WindowsUpdate.Tests;

public class WindowsUpdateSettingsReaderTests
{
    private static readonly string[] SingleIds =
        ["auto-update-mode", "no-auto-reboot", "exclude-drivers", "delivery-optimization"];

    [Fact]
    public void ReadSingles_DefaultMachine_AllNotConfigured()
    {
        var reader = new WindowsUpdateSettingsReader(new FakeRegistryService());

        var settings = reader.ReadSingles();

        Assert.Equal(SingleIds, settings.Select(s => s.Id));
        Assert.All(settings, s =>
        {
            Assert.Equal(string.Empty, s.CurrentValue);
            Assert.False(s.IsConfigured);
        });
    }

    [Fact]
    public void ReadSingles_ConfiguredValues_ScanAsConfigured()
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "AUOptions", 2);
        registry.SetDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoRebootWithLoggedOnUsers", 1);
        registry.SetDWord(WindowsUpdateRegistryPaths.WindowsUpdatePoliciesKeyPath, "ExcludeWUDriversInQualityUpdate", 1);
        registry.SetDWord(WindowsUpdateRegistryPaths.DeliveryOptimizationPoliciesKeyPath, "DODownloadMode", 0);
        var reader = new WindowsUpdateSettingsReader(registry);

        Assert.All(reader.ReadSingles(), s => Assert.True(s.IsConfigured));
    }

    [Fact]
    public void ReadSingles_DifferentPolicyValue_IsNotConfigured()
    {
        // AUOptions=4 (auto install) is a real policy but not OUR configured state.
        var registry = new FakeRegistryService();
        registry.SetDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "AUOptions", 4);
        var reader = new WindowsUpdateSettingsReader(registry);

        var auMode = reader.ReadSingles().Single(s => s.Id == "auto-update-mode");

        Assert.Equal("4", auMode.CurrentValue);
        Assert.False(auMode.IsConfigured);
    }

    [Fact]
    public void ReadVersionPin_NoDisplayVersion_ReturnsEmpty()
    {
        var reader = new WindowsUpdateSettingsReader(new FakeRegistryService());
        Assert.Empty(reader.ReadVersionPin());
    }

    [Fact]
    public void ReadVersionPin_TargetReleaseVersionFirst_WithLiveDisplayVersion()
    {
        var registry = new FakeRegistryService();
        registry.SetString(WindowsUpdateRegistryPaths.CurrentVersionKeyPath, "DisplayVersion", "24H2");
        var reader = new WindowsUpdateSettingsReader(registry);

        var pin = reader.ReadVersionPin();

        Assert.Equal(3, pin.Count);
        Assert.All(pin, s => Assert.Equal("version-pin", s.Id));
        // Toggle value convention: the FIRST descriptor's configured value is the set value.
        Assert.Equal(("TargetReleaseVersion", "1", ChangeValueType.Registry_DWord),
            (pin[0].RegistryValueName, pin[0].ConfiguredValue, pin[0].ValueType));
        Assert.Equal(("ProductVersion", "Windows 11", ChangeValueType.Registry_String),
            (pin[1].RegistryValueName, pin[1].ConfiguredValue, pin[1].ValueType));
        Assert.Equal(("TargetReleaseVersionInfo", "24H2", ChangeValueType.Registry_String),
            (pin[2].RegistryValueName, pin[2].ConfiguredValue, pin[2].ValueType));
    }
}
