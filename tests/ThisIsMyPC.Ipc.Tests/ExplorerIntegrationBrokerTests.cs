using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Ipc.Tests;
public class ExplorerIntegrationBrokerTests
{
    [Theory]
    [InlineData(ShellIntegrationChanges.AbsentRegistrationKey)]
    [InlineData(ShellRegistryPaths.AbsentValue)]
    public void FullRegistrationBatchAndUndoAreAuthorized(string absent)
    {
        var changes = ShellIntegrationChanges.RegistrationValues.Select((value, index) => new ChangeDescriptor
        {
            ModuleId = "Explorer", SettingId = ShellIntegrationChanges.Registration + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DisplayName = "Register ExplorerPatcher as a shell extension",
            SystemLocation = value.Key + "\\" + value.Name,
            BeforeValue = absent, AfterValue = value.Value,
            BeforeDisplay = "Not registered", AfterDisplay = "Registered", ValueType = value.Type,
        }).ToArray();
        var result = BrokerRequestPolicy.Create(new() { Changes = changes });
        Assert.True(result.IsSuccess, result.ErrorMessage);
        foreach (var change in changes)
        {
            Assert.True(result.Value!.Authorizes(new() { Kind = BrokerCommandKind.ApplyChange, Change = change }));
            var reverse = change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue };
            Assert.True(BrokerRequestPolicy.Create(new() { Changes = [reverse] }).IsSuccess);
            Assert.True(result.Value.Authorizes(new() { Kind = BrokerCommandKind.RevertChange, Change = reverse }));
        }
        var driveMask = changes[^1];
        Assert.False(BrokerRequestPolicy.Create(new() { Changes = [driveMask with { AfterValue = "256" }] }).IsSuccess);
        Assert.False(BrokerRequestPolicy.Create(new() { Changes = [driveMask with { SystemLocation = @"HKLM\SOFTWARE\Other\DriveMask" }] }).IsSuccess);
        Assert.False(BrokerRequestPolicy.Create(new() { Changes = [driveMask with { SettingId = "ep-registration:0" }] }).IsSuccess);
    }

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
