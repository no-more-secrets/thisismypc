using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class WidgetsPolicyBrokerTests
{
    [Fact]
    public void WidgetsPolicy_AuthorizesApplyAndAbsentUndo()
    {
        var change = new ChangeDescriptor
        {
            ModuleId = "Explorer", SettingId = "allow-widgets", DisplayName = "Allow Widgets",
            SystemLocation = @"HKLM\SOFTWARE\Policies\Microsoft\Dsh\AllowNewsAndInterests",
            BeforeValue = "__absent__", AfterValue = "0", ValueType = ChangeValueType.Registry_DWord,
            BeforeDisplay = "Allowed", AfterDisplay = "Disabled",
        };
        Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change] }).IsSuccess);
        Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change with
            { BeforeValue = "0", AfterValue = "__absent__" }] }).IsSuccess);
        Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change with
            { SystemLocation = @"HKLM\SOFTWARE\Policies\Microsoft\Dsh\OtherValue" }] }).IsSuccess);
    }
}
