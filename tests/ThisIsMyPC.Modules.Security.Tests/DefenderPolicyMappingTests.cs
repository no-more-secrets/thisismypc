using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Policies;

namespace ThisIsMyPC.Modules.Security.Tests;

/// <summary>Independent fixtures from WindowsDefender.admx, checked on 2026-10-08.</summary>
public sealed class DefenderPolicyMappingTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void UndefinedCloudLevels_StayBlocked(int value)
    {
        var registry = new UiFakeRegistryService();
        const string key = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\MpEngine";
        registry.WriteDWord(key, "MpCloudBlockLevel", value);
        var policies = new PolicyControlStateReader(registry);
        var setting = SecurityCatalog.Settings.Single(s => s.Id == "cloud-block-level");
        Assert.True(policies.Read(SecurityCatalog.ModuleId, setting.Id, key + @"\MpCloudBlockLevel").BlocksChanges);
        var reader = new SecuritySettings(registry, policies);
        Assert.Null(reader.Read(setting).Option);
        Assert.Throws<InvalidOperationException>(() => reader.Create(setting, setting.Choices[0]));
    }

    [Theory]
    [InlineData("behavior-monitoring", "Real-Time Protection", "DisableBehaviorMonitoring", 0)]
    [InlineData("file-activity-monitoring", "Real-Time Protection", "DisableOnAccessProtection", 0)]
    [InlineData("download-scanning", "Real-Time Protection", "DisableIOAVProtection", 0)]
    [InlineData("script-scanning", "Real-Time Protection", "DisableScriptScanning", 0)]
    [InlineData("scan-on-protection-enable", "Real-Time Protection", "DisableScanOnRealtimeEnable", 0)]
    [InlineData("heuristic-detection", "Scan", "DisableHeuristics", 0)]
    [InlineData("archive-scanning", "Scan", "DisableArchiveScanning", 0)]
    [InlineData("network-file-scanning", "Scan", "DisableScanningNetworkFiles", 0)]
    [InlineData("update-before-scan", "Scan", "CheckForSignaturesBeforeRunningScan", 1)]
    [InlineData("intelligence-on-battery", "Signature Updates", "DisableScheduledSignatureUpdateOnBattery", 0)]
    [InlineData("intelligence-on-startup", "Signature Updates", "UpdateOnStartUp", 1)]
    public void BooleanPolicies_DetectWindowsValuesAndStageOppositeChoice(string id, string subkey, string name, int on)
    {
        var registry = new UiFakeRegistryService();
        var key = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\" + subkey;
        var setting = SecurityCatalog.Settings.Single(s => s.Id == id);
        var reader = new SecuritySettings(registry);
        Assert.Equal("default", reader.Read(setting).Option?.Id);
        registry.WriteDWord(key, name, on);
        Assert.Equal("on", reader.Read(setting).Option?.Id);
        var change = Assert.Single(reader.Create(setting, setting.Choices.Single(c => c.Id == "off")).Changes);
        Assert.Equal(key + "\\" + name, change.SystemLocation);
        Assert.Equal(ChangeValueType.Registry_DWord, change.ValueType);
        Assert.Equal(on == 0 ? "1" : "0", change.AfterValue);
        registry.WriteDWord(key, name, 1 - on);
        Assert.Equal("off", reader.Read(setting).Option?.Id);
    }

    [Theory]
    [InlineData(0, "normal")]
    [InlineData(1, "moderate")]
    [InlineData(2, "high")]
    [InlineData(4, "high-plus")]
    [InlineData(6, "zero-tolerance")]
    public void CloudLevel_PreservesTheNonconsecutiveWindowsValues(int value, string option)
    {
        var registry = new UiFakeRegistryService();
        registry.WriteDWord(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\MpEngine", "MpCloudBlockLevel", value);
        var setting = SecurityCatalog.Settings.Single(s => s.Id == "cloud-block-level");
        var reader = new SecuritySettings(registry);
        Assert.Equal(option, reader.Read(setting).Option?.Id);
        var change = Assert.Single(reader.Create(setting, setting.Choices.Single(c => c.Id == "default")).Changes);
        Assert.Equal(value.ToString(System.Globalization.CultureInfo.InvariantCulture), change.BeforeValue);
        Assert.Equal("", change.AfterValue);
    }
}
