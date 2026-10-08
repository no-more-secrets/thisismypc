using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Modules.Security;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class SecurityBrokerTests
{
    [Fact]
    public void SecurityCatalog_AuthorizesOnlyItsExactOperations()
    {
        foreach (var setting in SecurityCatalog.Settings)
        foreach (var option in setting.Choices)
        for (var index = 0; index < setting.Targets.Count; index++)
        {
            var target = setting.Targets[index];
            var change = new Core.Changes.ChangeDescriptor
            {
                ModuleId = "Security", SettingId = setting.Id, DisplayName = setting.Title,
                SystemLocation = target.Location, ValueType = target.Type, BeforeValue = "", AfterValue = option.Values[index],
                BeforeDisplay = "Not configured", AfterDisplay = option.Label,
                Enforcement = new SettingEnforcement { SkuRestriction = setting.Edition },
            };
            var result = BrokerRequestPolicy.Create(new() { Changes = [change] });
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change with { SystemLocation = @"HKLM\Software\Other\Value" }] }).IsSuccess);
            Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change with { Enforcement = change.Enforcement with { OwnerModeRequired = true } }] }).IsSuccess);
        }
    }
}
