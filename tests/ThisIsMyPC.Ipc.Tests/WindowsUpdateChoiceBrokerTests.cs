using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.WindowsUpdate;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class WindowsUpdateChoiceBrokerTests
{
    [Theory]
    [InlineData("auto-update-mode", "ScheduledInstallDay", "2")]
    [InlineData("auto-update-mode", "ScheduledInstallTime", "20")]
    [InlineData("active-hours-manual", "ActiveHoursStart", "20")]
    [InlineData("active-hours-manual", "ActiveHoursEnd", "7")]
    public void ExactScheduleTargetsAllowApplyAndAbsentUndo(string id, string name, string value)
    {
        var change = new ChangeDescriptor
        {
            ModuleId = "Windows Update", SettingId = id, DisplayName = name,
            SystemLocation = (id == "auto-update-mode" ? WindowsUpdateRegistryPaths.AuPoliciesKeyPath : WindowsUpdateRegistryPaths.UxSettingsKeyPath) + "\\" + name,
            BeforeValue = "", AfterValue = value, BeforeDisplay = "Default", AfterDisplay = value,
            ValueType = ChangeValueType.Registry_DWord, Category = ChangeCategory.Modify,
        };
        Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change] }).IsSuccess);
        Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change with { BeforeValue = value, AfterValue = "" }] }).IsSuccess);
        Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change with { SystemLocation = change.SystemLocation + "Other" }] }).IsSuccess);
    }
}
