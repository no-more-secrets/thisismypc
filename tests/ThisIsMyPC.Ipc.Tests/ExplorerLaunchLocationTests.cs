using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell;
namespace ThisIsMyPC.Ipc.Tests;
public sealed class ExplorerLaunchLocationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LaunchToAllowsWindowsAndLegacyHistoryLocations(bool advanced)
    {
        var change = new ChangeDescriptor
        {
            ModuleId = "Explorer", SettingId = "launch-to", DisplayName = "Open Explorer to This PC",
            SystemLocation = (advanced ? ShellRegistryPaths.AdvancedKeyPath : ShellRegistryPaths.ExplorerKeyPath) + @"\LaunchTo",
            BeforeValue = "1", AfterValue = "2", BeforeDisplay = "Enabled", AfterDisplay = "Disabled", ValueType = ChangeValueType.Registry_DWord,
            Category = ChangeCategory.Disable,
        };
        Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change] }).IsSuccess);
    }
}
