using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Tests.Changes;

public sealed class TaskbarChangeFactoryTests
{
    private static TaskbarSettings MakeTaskbar(int alignment = 1, bool widgets = true, bool classicMenu = false, bool classicCommandBar = false) =>
        new(alignment, widgets, classicMenu, classicCommandBar);

    [Fact]
    public void CreateAlignmentChange_left_to_center()
    {
        var taskbar = MakeTaskbar(alignment: 0);
        var change = TaskbarChangeFactory.CreateAlignmentChange(taskbar, newAlignment: 1);

        Assert.Equal("Explorer", change.ModuleId);
        Assert.Equal("taskbar-alignment", change.SettingId);
        Assert.Equal("0", change.BeforeValue);
        Assert.Equal("1", change.AfterValue);
        Assert.Equal("Left", change.BeforeDisplay);
        Assert.Equal("Center", change.AfterDisplay);
        Assert.Equal(ChangeValueType.Registry_DWord, change.ValueType);
        Assert.Equal(ChangeCategory.Modify, change.Category);
        Assert.Equal(RestartRequirement.None, change.RestartRequirement);
    }

    [Fact]
    public void CreateAlignmentChange_center_to_left()
    {
        var taskbar = MakeTaskbar(alignment: 1);
        var change = TaskbarChangeFactory.CreateAlignmentChange(taskbar, newAlignment: 0);

        Assert.Equal("1", change.BeforeValue);
        Assert.Equal("0", change.AfterValue);
        Assert.Equal("Center", change.BeforeDisplay);
        Assert.Equal("Left", change.AfterDisplay);
    }

    [Fact]
    public void CreateAlignmentChange_rejects_invalid_value()
    {
        var taskbar = MakeTaskbar();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TaskbarChangeFactory.CreateAlignmentChange(taskbar, newAlignment: 99));
    }

    [Fact]
    public void CreateAlignmentChange_system_location_points_to_TaskbarAl()
    {
        var taskbar = MakeTaskbar();
        var change = TaskbarChangeFactory.CreateAlignmentChange(taskbar, newAlignment: 0);

        Assert.EndsWith(@"\TaskbarAl", change.SystemLocation);
    }

    [Fact]
    public void CreateWidgetsToggle_enable()
    {
        var taskbar = MakeTaskbar(widgets: false);
        var change = TaskbarChangeFactory.CreateWidgetsToggle(taskbar, enable: true);

        Assert.Equal("taskbar-widgets", change.SettingId);
        Assert.Equal("0", change.BeforeValue);
        Assert.Equal("1", change.AfterValue);
        Assert.Equal("Hidden", change.BeforeDisplay);
        Assert.Equal("Shown", change.AfterDisplay);
        Assert.Equal(ChangeCategory.Enable, change.Category);
        Assert.Equal(RestartRequirement.None, change.RestartRequirement);
    }

    [Fact]
    public void CreateWidgetsToggle_disable()
    {
        var taskbar = MakeTaskbar(widgets: true);
        var change = TaskbarChangeFactory.CreateWidgetsToggle(taskbar, enable: false);

        Assert.Equal("1", change.BeforeValue);
        Assert.Equal("0", change.AfterValue);
        Assert.Equal(ChangeCategory.Disable, change.Category);
    }

    [Fact]
    public void CreateClassicContextMenuToggle_enable()
    {
        var taskbar = MakeTaskbar(classicMenu: false);
        var change = TaskbarChangeFactory.CreateClassicContextMenuToggle(taskbar, enable: true);

        Assert.Equal("classic-context-menu", change.SettingId);
        Assert.Equal(ShellRegistryPaths.AbsentValue, change.BeforeValue);
        Assert.Equal("", change.AfterValue);
        Assert.Equal("Disabled", change.BeforeDisplay);
        Assert.Equal("Enabled", change.AfterDisplay);
        Assert.Equal(ChangeCategory.Enable, change.Category);
        Assert.Equal(RestartRequirement.ExplorerRestart, change.RestartRequirement);
    }

    [Fact]
    public void CreateClassicContextMenuToggle_disable()
    {
        var taskbar = MakeTaskbar(classicMenu: true);
        var change = TaskbarChangeFactory.CreateClassicContextMenuToggle(taskbar, enable: false);

        Assert.Equal("", change.BeforeValue);
        Assert.Equal(ShellRegistryPaths.AbsentValue, change.AfterValue);
        Assert.Equal("Enabled", change.BeforeDisplay);
        Assert.Equal("Disabled", change.AfterDisplay);
        Assert.Equal(ChangeCategory.Disable, change.Category);
        Assert.Equal(RestartRequirement.ExplorerRestart, change.RestartRequirement);
    }

    [Fact]
    public void CreateClassicContextMenuToggle_system_location_uses_shared_constant()
    {
        var taskbar = MakeTaskbar();
        var change = TaskbarChangeFactory.CreateClassicContextMenuToggle(taskbar, enable: true);

        Assert.Equal(ShellRegistryPaths.ClassicContextMenuKeyPath, change.SystemLocation);
    }

    [Fact]
    public void CreateCommandBarToggle_enable()
    {
        var taskbar = MakeTaskbar(classicCommandBar: false);
        var change = TaskbarChangeFactory.CreateCommandBarToggle(taskbar, enable: true);

        Assert.Equal("classic-command-bar", change.SettingId);
        Assert.Equal(ShellRegistryPaths.AbsentValue, change.BeforeValue);
        Assert.Equal("", change.AfterValue);
        Assert.Equal("Modern toolbar", change.BeforeDisplay);
        Assert.Equal("Classic ribbon", change.AfterDisplay);
        Assert.Equal(ChangeCategory.Enable, change.Category);
        Assert.Equal(RestartRequirement.ExplorerRestart, change.RestartRequirement);
    }

    [Fact]
    public void CreateCommandBarToggle_disable()
    {
        var taskbar = MakeTaskbar(classicCommandBar: true);
        var change = TaskbarChangeFactory.CreateCommandBarToggle(taskbar, enable: false);

        Assert.Equal("", change.BeforeValue);
        Assert.Equal(ShellRegistryPaths.AbsentValue, change.AfterValue);
        Assert.Equal("Classic ribbon", change.BeforeDisplay);
        Assert.Equal("Modern toolbar", change.AfterDisplay);
        Assert.Equal(ChangeCategory.Disable, change.Category);
    }

    [Fact]
    public void CreateCommandBarToggle_system_location_uses_shared_constant()
    {
        var taskbar = MakeTaskbar();
        var change = TaskbarChangeFactory.CreateCommandBarToggle(taskbar, enable: true);

        Assert.Equal(ShellRegistryPaths.CommandBarKeyPath, change.SystemLocation);
    }

    [Fact]
    public void CreateSearchboxModeChange_hidden_to_icon_only()
    {
        var taskbar = MakeTaskbar() with { SearchboxMode = 0 };
        var change = TaskbarChangeFactory.CreateSearchboxModeChange(taskbar, newMode: 1);

        Assert.Equal("taskbar-search-mode", change.SettingId);
        Assert.Equal($@"{ShellRegistryPaths.SearchKeyPath}\SearchboxTaskbarMode", change.SystemLocation);
        Assert.Equal("0", change.BeforeValue);
        Assert.Equal("1", change.AfterValue);
        Assert.Equal("Hidden", change.BeforeDisplay);
        Assert.Equal("Icon only", change.AfterDisplay);
        Assert.Equal(ChangeValueType.Registry_DWord, change.ValueType);
        Assert.Equal(ChangeCategory.Modify, change.Category);
        Assert.Equal(RestartRequirement.ExplorerRestart, change.RestartRequirement);
    }

    [Fact]
    public void CreateSearchboxModeChange_rejects_unknown_modes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TaskbarChangeFactory.CreateSearchboxModeChange(MakeTaskbar(), newMode: 4));
    }

    [Fact]
    public void CreateSearchboxModeChange_unknown_current_value_displays_raw_number()
    {
        var taskbar = MakeTaskbar() with { SearchboxMode = 9 };
        var change = TaskbarChangeFactory.CreateSearchboxModeChange(taskbar, newMode: 3);

        Assert.Equal("9", change.BeforeDisplay);
        Assert.Equal("Search box", change.AfterDisplay);
    }

    [Fact]
    public void CreateButtonCombiningChange_always_to_never()
    {
        var taskbar = MakeTaskbar(); // ButtonCombining defaults to 0
        var change = TaskbarChangeFactory.CreateButtonCombiningChange(taskbar, newLevel: 2);

        Assert.Equal("taskbar-button-combining", change.SettingId);
        Assert.Equal($@"{ShellRegistryPaths.AdvancedKeyPath}\TaskbarGlomLevel", change.SystemLocation);
        Assert.Equal("0", change.BeforeValue);
        Assert.Equal("2", change.AfterValue);
        Assert.Equal("Always, hide labels", change.BeforeDisplay);
        Assert.Equal("Never", change.AfterDisplay);
        Assert.Equal(ChangeCategory.Modify, change.Category);
        Assert.Equal(RestartRequirement.ExplorerRestart, change.RestartRequirement);
    }

    [Fact]
    public void CreateButtonCombiningChange_rejects_unknown_levels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TaskbarChangeFactory.CreateButtonCombiningChange(MakeTaskbar(), newLevel: 3));
    }
}
