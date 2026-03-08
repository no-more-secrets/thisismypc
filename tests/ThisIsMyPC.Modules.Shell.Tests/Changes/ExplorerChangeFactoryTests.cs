using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Tests.Changes;

public sealed class ExplorerChangeFactoryTests
{
    private static ExplorerPreference MakePreference(
        string id = "hidden-files",
        string currentValue = "2",
        string enabledValue = "1",
        string disabledValue = "2",
        bool isEnabled = false) =>
        new(
            Id: id,
            DisplayName: "Show hidden files",
            Description: "Display hidden files",
            RegistryKeyPath: @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName: "Hidden",
            ValueType: ChangeValueType.Registry_DWord,
            CurrentValue: currentValue,
            EnabledValue: enabledValue,
            DisabledValue: disabledValue,
            IsEnabled: isEnabled,
            RestartRequirement: RestartRequirement.ExplorerRefresh);

    [Fact]
    public void CreateToggle_enable_sets_correct_values()
    {
        var pref = MakePreference(currentValue: "2", isEnabled: false);
        var change = ExplorerChangeFactory.CreateToggle(pref, enable: true);

        Assert.Equal("Shell & Explorer", change.ModuleId);
        Assert.Equal("hidden-files", change.SettingId);
        Assert.Equal("Show hidden files", change.DisplayName);
        Assert.Equal(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\Hidden", change.SystemLocation);
        Assert.Equal("2", change.BeforeValue);
        Assert.Equal("1", change.AfterValue);
        Assert.Equal(ChangeValueType.Registry_DWord, change.ValueType);
        Assert.Equal(ChangeCategory.Enable, change.Category);
    }

    [Fact]
    public void CreateToggle_disable_sets_correct_values()
    {
        var pref = MakePreference(currentValue: "1", isEnabled: true);
        var change = ExplorerChangeFactory.CreateToggle(pref, enable: false);

        Assert.Equal("2", change.AfterValue);
        Assert.Equal("1", change.BeforeValue);
        Assert.Equal(ChangeCategory.Disable, change.Category);
    }

    [Fact]
    public void CreateToggle_sets_display_values()
    {
        var pref = MakePreference(isEnabled: false);
        var change = ExplorerChangeFactory.CreateToggle(pref, enable: true);

        Assert.Equal("Disabled", change.BeforeDisplay);
        Assert.Equal("Enabled", change.AfterDisplay);
    }
}
