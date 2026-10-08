using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Changes;

namespace ThisIsMyPC.Ipc.Tests;
public class ExplorerIntegrationBrokerTests
{
    [Theory]
    [InlineData(ShellIntegrationChanges.Taskbar, "2")]
    [InlineData(ShellIntegrationChanges.Corners, "absent")]
    public void NativeSettingsAllowOnlyTheTwoFixedOperations(string target, string before)
    {
        var change = ShellIntegrationChanges.Native(target, before, true);
        Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change] }).IsSuccess);
        Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change with { BeforeValue = change.AfterValue!, AfterValue = before }] }).IsSuccess);
        Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change with { SystemLocation = "shell:foreign" }] }).IsSuccess);
        Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change with { AfterValue = "malformed" }] }).IsSuccess);
    }
    [Fact]
    public void RegistrationDoesNotAuthorizeAnotherDllOrClsid()
    {
        var value = ShellIntegrationChanges.RegistrationValues[0];
        var change = new ChangeDescriptor { ModuleId = "Explorer", SettingId = ShellIntegrationChanges.Registration + "0",
            DisplayName = "Register", SystemLocation = value.Key + "\\" + value.Name,
            BeforeValue = ShellIntegrationChanges.AbsentRegistrationKey, AfterValue = value.Value,
            BeforeDisplay = "Off", AfterDisplay = "On", ValueType = value.Type };
        Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change] }).IsSuccess);
        Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change with { AfterValue = @"C:\foreign.dll" }] }).IsSuccess);
        Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change with { SystemLocation = @"HKLM\Software\Other\" }] }).IsSuccess);
    }
}
