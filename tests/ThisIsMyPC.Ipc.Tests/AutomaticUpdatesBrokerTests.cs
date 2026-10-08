using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Modules.WindowsUpdate.Services;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class AutomaticUpdatesBrokerTests
{
    [Fact]
    public void OnlyExactPolicyAndValuesAreAllowed()
    {
        var change = new ChangeDescriptor
        {
            ModuleId = "Windows Update", SettingId = AutomaticUpdatesSetting.Id, DisplayName = "Automatic Updates",
            SystemLocation = AutomaticUpdatesSetting.Location, ValueType = ChangeValueType.LocalPolicy_DWord,
            BeforeValue = new LocalPolicyValue(null, "").Encode(), AfterValue = new LocalPolicyValue("1", "1").Encode(),
            BeforeDisplay = "Enabled", AfterDisplay = "Disabled", Enforcement = new SettingEnforcement { SkuRestriction = WindowsSku.Pro },
        };
        Assert.True(BrokerOperationRules.Allows(change));
        Assert.True(BrokerOperationRules.Allows(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue }));
        Assert.False(BrokerOperationRules.Allows(change with { SystemLocation = AutomaticUpdatesSetting.Location + "Other" }));
        Assert.False(BrokerOperationRules.Allows(change with { AfterValue = new LocalPolicyValue("2", "2").Encode() }));
        Assert.False(BrokerOperationRules.Allows(change with { ValueType = ChangeValueType.Registry_DWord, BeforeValue = "0", AfterValue = "1" }));
    }
}
