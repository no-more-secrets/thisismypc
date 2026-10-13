using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Ipc.Tests;

public class SettingsAppearanceBrokerTests
{
    [Theory]
    [InlineData("hide-settings-home", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\SettingsPageVisibility", "__absent__", "hide:home", ChangeValueType.Registry_String)]
    [InlineData("dark-apps", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme", "1", "0", ChangeValueType.Registry_DWord)]
    [InlineData("dark-windows", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\SystemUsesLightTheme", "1", "0", ChangeValueType.Registry_DWord)]
    public void AppearanceChanges_AuthorizeApplyAndExactUndo(string id, string location, string before, string after, ChangeValueType type)
    {
        var change = new ChangeDescriptor
        {
            ModuleId = "Explorer", SettingId = id, DisplayName = id, SystemLocation = location,
            BeforeValue = before, AfterValue = after, ValueType = type,
            BeforeDisplay = "Before", AfterDisplay = "After",
        };
        Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change] }).IsSuccess);
        Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change with
        {
            BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue,
        }] }).IsSuccess);
        Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change with
        {
            SystemLocation = @"HKCU\Software\Other\SettingsPageVisibility",
        }] }).IsSuccess);
    }
}
