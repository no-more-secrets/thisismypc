using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Policies;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class LocalPolicyToggleBrokerTests
{
    [Theory]
    [InlineData("windows-tips")]
    [InlineData("consumer-features")]
    [InlineData("silent-app-installs")]
    public void LocalToggle_OnlyAuthorizesExactPolicy(string id)
    {
        var change = new ChangeDescriptor
        {
            ModuleId = "Windows Annoyances", SettingId = id, DisplayName = id,
            SystemLocation = LocalPolicyToggleCatalog.Location("Windows Annoyances", id)!,
            ValueType = ChangeValueType.LocalPolicy_DWord,
            BeforeValue = new LocalPolicyValue("1", "1").Encode(), AfterValue = new LocalPolicyValue("0", "0").Encode(),
            BeforeDisplay = "Suppressed", AfterDisplay = "Allowed",
            Enforcement = new SettingEnforcement { SkuRestriction = WindowsSku.Enterprise },
        };
        Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change] }).IsSuccess);
        foreach (var forged in new[] {
            change with { SystemLocation = @"HKLM\SOFTWARE\Other\Value" },
            change with { SettingId = "other" },
            change with { AfterValue = new LocalPolicyValue("2", "2").Encode() },
            change with { AfterValue = "{}" },
            change with { Enforcement = change.Enforcement with { OwnerModeRequired = true } },
            change with { Enforcement = change.Enforcement with { SkuRestriction = WindowsSku.Pro } },
            change with { ValueType = ChangeValueType.LocalPolicy_String } })
            Assert.False(BrokerRequestPolicy.Create(new() { Changes = [forged] }).IsSuccess);
    }
}
