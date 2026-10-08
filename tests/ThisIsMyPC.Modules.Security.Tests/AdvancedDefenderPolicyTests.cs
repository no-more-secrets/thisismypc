using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Policies;

namespace ThisIsMyPC.Modules.Security.Tests;

/// <summary>WindowsDefender.admx and WindowsDefenderSecurityCenter.admx fixtures, checked 2026-10-08.</summary>
public sealed class AdvancedDefenderPolicyTests
{
    [Theory]
    [InlineData("defender-startup-priority", "", "AllowFastServiceStartup", 1, "normal", "low")]
    [InlineData("automatic-remediation", "", "DisableRoutinelyTakingAction", 0, "on", "off")]
    [InlineData("file-hash-computation", "MpEngine", "EnableFileHashComputation", 1, "on", "off")]
    [InlineData("randomize-schedules", "", "RandomizeScheduleTaskTimes", 1, "on", "off")]
    [InlineData("restore-before-cleaning", "Scan", "DisableRestorePoint", 0, "on", "off")]
    [InlineData("catchup-full-scan", "Scan", "DisableCatchupFullScan", 0, "on", "off")]
    [InlineData("catchup-quick-scan", "Scan", "DisableCatchupQuickScan", 0, "on", "off")]
    [InlineData("reparse-point-scanning", "Scan", "DisableReparsePointScanning", 0, "on", "off")]
    [InlineData("rapid-intelligence", "Signature Updates", "RealtimeSignatureDelivery", 1, "on", "off")]
    [InlineData("intelligence-from-microsoft-update", "Signature Updates", "ForceUpdateFromMU", 1, "on", "off")]
    [InlineData("cloud-signature-corrections", "Signature Updates", "SignatureDisableNotification", 1, "on", "off")]
    [InlineData("scan-after-intelligence-update", "Signature Updates", "DisableScanOnUpdate", 0, "on", "off")]
    [InlineData("defender-error-reporting", "Reporting", "DisableGenericRePorts", 0, "on", "off")]
    [InlineData("defender-enhanced-notifications", "Reporting", "DisableEnhancedNotifications", 0, "on", "off")]
    [InlineData("defender-reboot-notifications", "UX Configuration", "SuppressRebootNotification", 0, "on", "off")]
    [InlineData("merge-local-defender-lists", "", "DisableLocalAdminMerge", 0, "on", "off")]
    [InlineData("override-cloud-protection", "Spynet", "LocalSettingOverrideSpynetReporting", 1, "local", "policy")]
    [InlineData("override-file-monitoring", "Real-Time Protection", "LocalSettingOverrideDisableOnAccessProtection", 1, "local", "policy")]
    [InlineData("override-scan-direction", "Real-Time Protection", "LocalSettingOverrideRealtimeScanDirection", 1, "local", "policy")]
    [InlineData("override-download-scanning", "Real-Time Protection", "LocalSettingOverrideDisableIOAVProtection", 1, "local", "policy")]
    [InlineData("override-behavior-monitoring", "Real-Time Protection", "LocalSettingOverrideDisableBehaviorMonitoring", 1, "local", "policy")]
    [InlineData("override-real-time-protection", "Real-Time Protection", "LocalSettingOverrideDisableRealtimeMonitoring", 1, "local", "policy")]
    public void BooleanMappings_ReadBothStatesAndTargetOnlyThePolicy(string id, string subkey, string name, int value, string selected, string opposite)
    {
        var registry = new UiFakeRegistryService();
        var key = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender" + (subkey.Length == 0 ? "" : "\\" + subkey);
        var setting = SecurityCatalog.Settings.Single(s => s.Id == id);
        var reader = new SecuritySettings(registry);
        Assert.Equal("default", reader.Read(setting).Option?.Id);
        registry.WriteDWord(key, name, value);
        Assert.Equal(selected, reader.Read(setting).Option?.Id);
        var change = Assert.Single(reader.Create(setting, setting.Choices.Single(o => o.Id == opposite)).Changes);
        Assert.Equal(key + "\\" + name, change.SystemLocation);
        Assert.Equal(ChangeValueType.Registry_DWord, change.ValueType);
        Assert.Equal(value == 0 ? "1" : "0", change.AfterValue);
        registry.WriteDWord(key, name, 1 - value);
        Assert.Equal(opposite, reader.Read(setting).Option?.Id);
    }

    [Theory]
    [InlineData(0, "both")]
    [InlineData(1, "incoming")]
    [InlineData(2, "outgoing")]
    [InlineData(3, null)]
    public void ScanDirection_UsesNtfsDirectionValues(int value, string? expected)
    {
        var registry = new UiFakeRegistryService();
        const string key = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection";
        registry.WriteDWord(key, "RealtimeScanDirection", value);
        var setting = SecurityCatalog.Settings.Single(s => s.Id == "scan-direction");
        var policies = new PolicyControlStateReader(registry);
        var state = new SecuritySettings(registry, policies).Read(setting);
        Assert.Equal(expected, state.Option?.Id);
        Assert.Equal(expected is null, state.BlockReason is not null);
    }

    [Fact]
    public void ExploitProtectionEditing_DoesNotConfigureProtectionItself()
    {
        var registry = new UiFakeRegistryService();
        const string key = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender Security Center\App and Browser protection";
        registry.WriteDWord(key, "DisallowExploitProtectionOverride", 1);
        var setting = SecurityCatalog.Settings.Single(s => s.Id == "allow-exploit-protection-edits");
        var reader = new SecuritySettings(registry);
        Assert.Equal("off", reader.Read(setting).Option?.Id);
        var change = Assert.Single(reader.Create(setting, setting.Choices.Single(o => o.Id == "on")).Changes);
        Assert.Equal(key + @"\DisallowExploitProtectionOverride", change.SystemLocation);
        Assert.Equal("0", change.AfterValue);
    }
}
