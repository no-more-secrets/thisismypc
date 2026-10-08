using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Network;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class NetworkBrokerTests
{
    [Theory]
    [InlineData("adapter/ff0b8c19-21f2-446f-bd31-cc2fb5682fc2", "true", "false")]
    [InlineData("dns/ff0b8c19-21f2-446f-bd31-cc2fb5682fc2", "<automatic>", "1.1.1.1")]
    [InlineData("firewall/4", "true", "false")]
    public void BrokerAdmitsOnlyDefinedNetworkOperations(string target, string before, string after)
    {
        var change = NetworkChanges.Create(target, "Network", before, after);
        Assert.True(BrokerOperationRules.Allows(change));
        Assert.True(BrokerOperationRules.Allows(change with { BeforeValue = after, AfterValue = before }));
        Assert.False(BrokerOperationRules.Allows(change with { SystemLocation = "HKLM\\anything" }));
        Assert.False(BrokerOperationRules.Allows(change with { AfterValue = "bad" }));
        Assert.False(BrokerOperationRules.Allows(change with { ValueType = ChangeValueType.Registry_String }));
        Assert.False(BrokerOperationRules.Allows(change with { Category = ChangeCategory.Delete }));
    }
}
