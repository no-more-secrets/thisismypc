using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests;

public sealed class WidgetsDefaultsTests
{
    private static SetEntry HideWidgets => new()
    {
        ModuleId = "Explorer", SettingId = "taskbar-widgets", Value = "0", Description = "",
    };

    [Fact]
    public async Task MissingValue_StagesHideAndUndoRestoresWindowsDefault()
    {
        var registry = new FakeRegistryService();
        var inspector = new ShellSetEntryInspector(registry);
        Assert.True(new TaskbarSettingsReader(registry).Read().WidgetsEnabled);
        Assert.False(inspector.Inspect(HideWidgets)!.IsApplied);
        var change = Assert.Single(inspector.CreateChangeGroup(HideWidgets)!.Changes);
        Assert.Equal(ShellRegistryPaths.AbsentValue, change.BeforeValue);
        Assert.Equal("0", change.AfterValue);

        await using var module = new ShellModule(registry);
        Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
        Assert.True(inspector.Inspect(HideWidgets)!.IsApplied);
        Assert.True((await module.RevertChangeAsync(change with
            { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue })).IsSuccess);
        Assert.False(registry.ReadDWord(ShellRegistryPaths.AdvancedKeyPath, "TaskbarDa").IsSuccess);
        Assert.False(inspector.Inspect(HideWidgets)!.IsApplied);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void ExplicitValue_PreservesBeforeState(int value, bool alreadyHidden)
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(ShellRegistryPaths.AdvancedKeyPath, "TaskbarDa", value);
        var inspector = new ShellSetEntryInspector(registry);
        Assert.Equal(alreadyHidden, inspector.Inspect(HideWidgets)!.IsApplied);
        var change = Assert.Single(inspector.CreateChangeGroup(HideWidgets)!.Changes);
        Assert.Equal(value.ToString(), change.BeforeValue);
    }
}
