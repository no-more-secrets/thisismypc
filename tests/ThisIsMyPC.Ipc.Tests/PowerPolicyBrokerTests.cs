using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Modules.Power.Changes;
using ThisIsMyPC.Modules.Power.Services;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class PowerPolicyBrokerTests
{
    [Fact]
    public void AcceptsExactPowerPoliciesAndUndoOnly()
    {
        var change = new ChangeDescriptor
        {
            ModuleId = "Power Plans", SettingId = PowerPlanChangeFactory.ActivePlanPolicyPinSettingId,
            DisplayName = "Power plan pin", SystemLocation = PowerPolicySettings.PinLocation,
            ValueType = ChangeValueType.LocalPolicy_String,
            BeforeValue = new LocalPolicyValue("381b4222-f694-41f0-9685-ff5bb260df2e", "381b4222-f694-41f0-9685-ff5bb260df2e").Encode(),
            AfterValue = new LocalPolicyValue(null, "").Encode(), BeforeDisplay = "Pinned", AfterDisplay = "Not pinned",
            Enforcement = new SettingEnforcement { SkuRestriction = WindowsSku.Pro },
        };
        Assert.True(BrokerOperationRules.Allows(change));
        Assert.True(BrokerOperationRules.Allows(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue }));
        Assert.False(BrokerOperationRules.Allows(change with { AfterValue = new LocalPolicyValue("invalid", "invalid").Encode() }));
        Assert.False(BrokerOperationRules.Allows(change with { SystemLocation = change.SystemLocation + "Other" }));
        Assert.False(BrokerOperationRules.Allows(change with { Enforcement = null }));
        var sleep = change with
        {
            SettingId = PowerPlanChangeFactory.AllowSleepSettingId,
            SystemLocation = PowerPlanChangeFactory.AllowStandbyPolicyKeyPath + "\\ACSettingIndex",
            ValueType = ChangeValueType.LocalPolicy_DWord, BeforeValue = new LocalPolicyValue("0", "0").Encode(),
        };
        Assert.True(BrokerOperationRules.Allows(sleep));
        Assert.False(BrokerOperationRules.Allows(sleep with { AfterValue = new LocalPolicyValue("2", "2").Encode() }));
    }
}
