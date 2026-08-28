using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests;

public sealed class ShellSetEntryInspectorTests
{
    private readonly FakeRegistryService _registry = new();

    private ShellSetEntryInspector Inspector => new(_registry);

    private static SetEntry Entry(string settingId, string value) => new()
    {
        ModuleId = "Explorer",
        SettingId = settingId,
        Value = value,
        Description = "d",
    };

    [Fact]
    public void UnknownSettingId_ReturnsNull()
    {
        Assert.Null(Inspector.Inspect(Entry("no-such-setting", "0")));
    }

    [Fact]
    public void TaskbarAlignment_DefaultCenter_LeftEntryNotApplied()
    {
        var state = Inspector.Inspect(Entry("taskbar-alignment", "0"));

        Assert.Equal("Taskbar alignment", state!.SettingDisplayName);
        Assert.Equal("1", state.CurrentValue);
        Assert.Equal("Center", state.CurrentDisplay);
        Assert.False(state.IsApplied);
    }

    [Fact]
    public void TaskbarAlignment_AlreadyLeft_Applied()
    {
        _registry.SetDWord(ShellRegistryPaths.AdvancedKeyPath, "TaskbarAl", 0);

        var state = Inspector.Inspect(Entry("taskbar-alignment", "0"));

        Assert.Equal("Left", state!.CurrentDisplay);
        Assert.True(state.IsApplied);
    }

    [Fact]
    public void ClassicContextMenu_KeyAbsent_EnableEntryNotApplied()
    {
        var state = Inspector.Inspect(Entry("classic-context-menu", ""));

        Assert.Equal("Classic context menu", state!.SettingDisplayName);
        Assert.Equal(ShellRegistryPaths.AbsentValue, state.CurrentValue);
        Assert.Equal("Disabled", state.CurrentDisplay);
        Assert.False(state.IsApplied);
    }

    [Fact]
    public void ClassicContextMenu_KeyPresent_Applied()
    {
        _registry.AddKey(ShellRegistryPaths.ClassicContextMenuKeyPath);

        var state = Inspector.Inspect(Entry("classic-context-menu", ""));

        Assert.Equal("", state!.CurrentValue);
        Assert.Equal("Enabled", state.CurrentDisplay);
        Assert.True(state.IsApplied);
    }

    [Fact]
    public void ClassicCommandBar_KeyAbsent_ShowsModernToolbar()
    {
        var state = Inspector.Inspect(Entry("classic-command-bar", ""));

        Assert.Equal("Modern toolbar", state!.CurrentDisplay);
        Assert.False(state.IsApplied);
    }

    [Fact]
    public void TaskbarWidgets_MissingValueScansAsHidden_HideEntryApplied()
    {
        // TaskbarSettingsReader treats a missing TaskbarDa as widgets-off
        var state = Inspector.Inspect(Entry("taskbar-widgets", "0"));

        Assert.Equal("Hidden", state!.CurrentDisplay);
        Assert.Equal("0", state.CurrentValue);
        Assert.True(state.IsApplied);

        _registry.SetDWord(ShellRegistryPaths.AdvancedKeyPath, "TaskbarDa", 1);
        var shown = Inspector.Inspect(Entry("taskbar-widgets", "0"));
        Assert.Equal("Shown", shown!.CurrentDisplay);
        Assert.False(shown.IsApplied);
    }

    [Fact]
    public void CreateChangeGroup_TaskbarAlignmentLeft_BuildsSingleDescriptor()
    {
        var group = Inspector.CreateChangeGroup(Entry("taskbar-alignment", "0"));

        var change = Assert.Single(group!.Changes);
        Assert.Equal("taskbar-alignment", change.SettingId);
        Assert.Equal("0", change.AfterValue);
        Assert.Equal("1", change.BeforeValue); // live read: missing value = Center
    }

    [Fact]
    public void CreateChangeGroup_ClassicContextMenuEnable_CarriesShimEnforcement()
    {
        var group = Inspector.CreateChangeGroup(Entry("classic-context-menu", ""));

        var change = Assert.Single(group!.Changes);
        Assert.Equal("", change.AfterValue);
        Assert.Equal(ShellRegistryPaths.AbsentValue, change.BeforeValue);
        Assert.NotNull(change.Enforcement);
    }

    [Fact]
    public void CreateChangeGroup_ClassicCommandBar_BothDirections()
    {
        var enable = Inspector.CreateChangeGroup(Entry("classic-command-bar", ""));
        Assert.Equal("", Assert.Single(enable!.Changes).AfterValue);

        var disable = Inspector.CreateChangeGroup(Entry("classic-command-bar", ShellRegistryPaths.AbsentValue));
        Assert.Equal(ShellRegistryPaths.AbsentValue, Assert.Single(disable!.Changes).AfterValue);
    }

    [Fact]
    public void CreateChangeGroup_ExplorerPreference_MapsEnableDirection()
    {
        var group = Inspector.CreateChangeGroup(Entry("hidden-files", "1"));

        var change = Assert.Single(group!.Changes);
        Assert.Equal("hidden-files", change.SettingId);
        Assert.Equal("1", change.AfterValue);
    }

    [Fact]
    public void CreateChangeGroup_BogusValues_ReturnNull()
    {
        Assert.Null(Inspector.CreateChangeGroup(Entry("taskbar-alignment", "5")));
        Assert.Null(Inspector.CreateChangeGroup(Entry("classic-context-menu", "banana")));
        Assert.Null(Inspector.CreateChangeGroup(Entry("hidden-files", "banana")));
        Assert.Null(Inspector.CreateChangeGroup(Entry("no-such-setting", "0")));
    }

    [Fact]
    public void ExplorerPreference_ResolvesThroughReader()
    {
        // hidden-files: default 2 (hidden), enabled value 1
        var state = Inspector.Inspect(Entry("hidden-files", "1"));

        Assert.NotNull(state);
        Assert.Equal("2", state!.CurrentValue);
        Assert.Equal("Disabled", state.CurrentDisplay);
        Assert.False(state.IsApplied);

        _registry.SetDWord(ShellRegistryPaths.AdvancedKeyPath, "Hidden", 1);
        var applied = Inspector.Inspect(Entry("hidden-files", "1"));
        Assert.Equal("Enabled", applied!.CurrentDisplay);
        Assert.True(applied.IsApplied);
    }
}
