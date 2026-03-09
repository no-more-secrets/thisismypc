using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Tests.Changes;

public sealed class ExplorerChangeFactoryTests
{
    private static ExplorerPreference MakePreference(
        string id = "hidden-files",
        string keyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
        string valueName = "Hidden",
        string currentValue = "2",
        string enabledValue = "1",
        string disabledValue = "2",
        bool isEnabled = false,
        RestartRequirement restart = RestartRequirement.ExplorerRefresh) =>
        new(
            Id: id,
            DisplayName: "Show hidden files",
            Description: "Display hidden files",
            RegistryKeyPath: keyPath,
            RegistryValueName: valueName,
            ValueType: ChangeValueType.Registry_DWord,
            CurrentValue: currentValue,
            EnabledValue: enabledValue,
            DisabledValue: disabledValue,
            IsEnabled: isEnabled,
            RestartRequirement: restart);

    [Fact]
    public void CreateToggle_enable_sets_correct_values()
    {
        var pref = MakePreference(currentValue: "2", isEnabled: false);
        var change = ExplorerChangeFactory.CreateToggle(pref, enable: true);

        Assert.Equal("Explorer", change.ModuleId);
        Assert.Equal("hidden-files", change.SettingId);
        Assert.Equal("Show hidden files", change.DisplayName);
        Assert.Equal(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\Hidden", change.SystemLocation);
        Assert.Equal("2", change.BeforeValue);
        Assert.Equal("1", change.AfterValue);
        Assert.Equal(ChangeValueType.Registry_DWord, change.ValueType);
        Assert.Equal(ChangeCategory.Enable, change.Category);
        Assert.Equal(RestartRequirement.ExplorerRefresh, change.RestartRequirement);
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

    // --- Navigation Pane (Task 2) ---

    [Fact]
    public void CreateToggle_NavPaneShowAllFolders_enable()
    {
        var pref = MakePreference(
            id: "nav-pane-show-all-folders",
            valueName: "NavPaneShowAllFolders",
            currentValue: "0",
            enabledValue: "1",
            disabledValue: "0",
            isEnabled: false,
            restart: RestartRequirement.ExplorerRestart);

        var change = ExplorerChangeFactory.CreateToggle(pref, enable: true);

        Assert.Equal(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\NavPaneShowAllFolders", change.SystemLocation);
        Assert.Equal("1", change.AfterValue);
        Assert.Equal(RestartRequirement.ExplorerRestart, change.RestartRequirement);
    }

    [Fact]
    public void CreateToggle_NavPaneShowAllFolders_disable()
    {
        var pref = MakePreference(
            id: "nav-pane-show-all-folders",
            valueName: "NavPaneShowAllFolders",
            currentValue: "1",
            enabledValue: "1",
            disabledValue: "0",
            isEnabled: true,
            restart: RestartRequirement.ExplorerRestart);

        var change = ExplorerChangeFactory.CreateToggle(pref, enable: false);

        Assert.Equal("0", change.AfterValue);
        Assert.Equal(ChangeCategory.Disable, change.Category);
    }

    [Fact]
    public void CreateToggle_NavPaneExpandToCurrentFolder_enable()
    {
        var pref = MakePreference(
            id: "nav-pane-expand-to-current",
            valueName: "NavPaneExpandToCurrentFolder",
            currentValue: "0",
            enabledValue: "1",
            disabledValue: "0",
            isEnabled: false,
            restart: RestartRequirement.ExplorerRestart);

        var change = ExplorerChangeFactory.CreateToggle(pref, enable: true);

        Assert.Equal(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\NavPaneExpandToCurrentFolder", change.SystemLocation);
        Assert.Equal("1", change.AfterValue);
        Assert.Equal(RestartRequirement.ExplorerRestart, change.RestartRequirement);
    }

    [Fact]
    public void CreateToggle_NavPaneExpandToCurrentFolder_disable()
    {
        var pref = MakePreference(
            id: "nav-pane-expand-to-current",
            valueName: "NavPaneExpandToCurrentFolder",
            currentValue: "1",
            enabledValue: "1",
            disabledValue: "0",
            isEnabled: true,
            restart: RestartRequirement.ExplorerRestart);

        var change = ExplorerChangeFactory.CreateToggle(pref, enable: false);

        Assert.Equal("0", change.AfterValue);
        Assert.Equal(ChangeCategory.Disable, change.Category);
    }

    // --- Compact View (Task 3) ---

    [Fact]
    public void CreateToggle_UseCompactMode_enable()
    {
        var pref = MakePreference(
            id: "compact-view",
            valueName: "UseCompactMode",
            currentValue: "0",
            enabledValue: "1",
            disabledValue: "0",
            isEnabled: false,
            restart: RestartRequirement.None);

        var change = ExplorerChangeFactory.CreateToggle(pref, enable: true);

        Assert.Equal(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\UseCompactMode", change.SystemLocation);
        Assert.Equal("1", change.AfterValue);
        Assert.Equal(RestartRequirement.None, change.RestartRequirement);
    }

    [Fact]
    public void CreateToggle_UseCompactMode_disable()
    {
        var pref = MakePreference(
            id: "compact-view",
            valueName: "UseCompactMode",
            currentValue: "1",
            enabledValue: "1",
            disabledValue: "0",
            isEnabled: true,
            restart: RestartRequirement.None);

        var change = ExplorerChangeFactory.CreateToggle(pref, enable: false);

        Assert.Equal("0", change.AfterValue);
        Assert.Equal(ChangeCategory.Disable, change.Category);
        Assert.Equal(RestartRequirement.None, change.RestartRequirement);
    }
}
