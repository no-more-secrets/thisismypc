using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Modules.Privacy.Services;

namespace ThisIsMyPC.Ipc.Tests;

public class DiagnosticDataBrokerTests
{
    [Fact]
    public void LocalDiagnosticChoiceRequiresTheUiAccountAndExactTarget()
    {
        var change = new ChangeDescriptor
        {
            ModuleId = "Privacy & Telemetry", SettingId = "telemetry-level", DisplayName = "Diagnostic data",
            SystemLocation = DiagnosticDataSetting.Location, ValueType = ChangeValueType.LocalPolicy_DWord,
            BeforeValue = new LocalPolicyValue("0", "0").Encode(), AfterValue = new LocalPolicyValue("3", "3").Encode(),
            BeforeDisplay = "Off", AfterDisplay = "Optional", Category = ChangeCategory.Modify,
            Enforcement = new SettingEnforcement { SkuRestriction = WindowsSku.Pro },
        };
        Assert.True(BrokerOperationRules.Allows(change));
        Assert.True(PrivilegedModuleHost.CanApplyProtectedChoice(change, "same", "same"));
        Assert.False(PrivilegedModuleHost.CanApplyProtectedChoice(change, "user", "admin"));
        Assert.False(PrivilegedModuleHost.CanApplyProtectedChoice(change, "user", null));
        Assert.False(BrokerOperationRules.Allows(change with { SystemLocation = DiagnosticDataSetting.Location + "Other" }));
        Assert.False(BrokerOperationRules.Allows(change with { AfterValue = new LocalPolicyValue("99", "99").Encode() }));
    }
}
