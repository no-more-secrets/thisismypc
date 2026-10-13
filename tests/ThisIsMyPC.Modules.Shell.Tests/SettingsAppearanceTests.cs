using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests;

public sealed class SettingsAppearanceTests
{
    private const string VisibilityKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private readonly FakeRegistryService _registry = new();

    private static SetEntry HideHome => new()
    {
        ModuleId = "Explorer", SettingId = "hide-settings-home", Value = "1", Description = "",
    };

    [Theory]
    [InlineData("hide-settings-home", VisibilityKey, "SettingsPageVisibility")]
    [InlineData("dark-apps", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme")]
    [InlineData("dark-windows", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme")]
    public void UnreadableBeforeState_IsNotStaged(string id, string key, string name)
    {
        _registry.SetReadFailure(key, name, ErrorCategory.AccessDenied);
        var inspector = new ShellSetEntryInspector(_registry);
        var entry = HideHome with { SettingId = id, Value = id == "hide-settings-home" ? "1" : "0" };
        Assert.Null(inspector.Inspect(entry));
        Assert.Null(inspector.CreateChangeGroup(entry));
    }

    [Theory]
    [InlineData(null, "hide:home")]
    [InlineData("hide:bluetooth;privacy", "hide:bluetooth;privacy;home")]
    [InlineData("hide:homepage", "hide:homepage;home")]
    public void HidingHome_PreservesOtherPagesAndExactBeforeState(string? before, string after)
    {
        if (before is not null) _registry.SetString(VisibilityKey, "SettingsPageVisibility", before);
        var inspector = new ShellSetEntryInspector(_registry);
        Assert.False(inspector.Inspect(HideHome)!.IsApplied);
        var change = Assert.Single(inspector.CreateChangeGroup(HideHome)!.Changes);
        Assert.Equal(before ?? ShellRegistryPaths.AbsentValue, change.BeforeValue);
        Assert.Equal(after, change.AfterValue);
        Assert.Equal(ChangeValueType.Registry_String, change.ValueType);
    }

    [Theory]
    [InlineData("hide:home", ShellRegistryPaths.AbsentValue)]
    [InlineData("hide:bluetooth;HOME;privacy", "hide:bluetooth;privacy")]
    public void ShowingHome_PreservesOtherHiddenPages(string before, string after)
    {
        _registry.SetString(VisibilityKey, "SettingsPageVisibility", before);
        var inspector = new ShellSetEntryInspector(_registry);
        Assert.True(inspector.Inspect(HideHome)!.IsApplied);
        var show = HideHome with { Value = "0" };
        var change = Assert.Single(inspector.CreateChangeGroup(show)!.Changes);
        Assert.Equal(before, change.BeforeValue);
        Assert.Equal(after, change.AfterValue);
    }

    [Fact]
    public void ShowOnlyPolicy_IsNotReplacedOrBroadened()
    {
        _registry.SetString(VisibilityKey, "SettingsPageVisibility", "showonly:home;about");
        var inspector = new ShellSetEntryInspector(_registry);
        Assert.Null(inspector.Inspect(HideHome));
        Assert.Null(inspector.CreateChangeGroup(HideHome));
    }

    [Fact]
    public void SearchBar_HiddenEntryPreservesCurrentMode()
    {
        _registry.SetDWord(ShellRegistryPaths.SearchKeyPath, "SearchboxTaskbarMode", 2);
        var entry = HideHome with { SettingId = "taskbar-search-mode", Value = "0" };
        var inspector = new ShellSetEntryInspector(_registry);
        Assert.False(inspector.Inspect(entry)!.IsApplied);
        var change = Assert.Single(inspector.CreateChangeGroup(entry)!.Changes);
        Assert.Equal("2", change.BeforeValue);
        Assert.Equal("0", change.AfterValue);
    }

    [Theory]
    [InlineData("hide:home")]
    [InlineData("hide:bluetooth;home")]
    public void ExportedHomeTarget_PreservesRecipientHiddenPages(string exported)
    {
        _registry.SetString(VisibilityKey, "SettingsPageVisibility", "hide:privacy");
        var entry = HideHome with { Value = exported };
        var inspector = new ShellSetEntryInspector(_registry);
        Assert.False(inspector.Inspect(entry)!.IsApplied);
        var change = Assert.Single(inspector.CreateChangeGroup(entry)!.Changes);
        Assert.Equal("hide:privacy;home", change.AfterValue);
    }

    [Theory]
    [InlineData("hide-settings-home", "1")]
    [InlineData("dark-apps", "0")]
    [InlineData("dark-windows", "0")]
    public async Task ApplyAndUndo_RestoresMissingRegistryValues(string id, string target)
    {
        await using var module = new ShellModule(_registry);
        var change = Assert.Single(new ShellSetEntryInspector(_registry)
            .CreateChangeGroup(HideHome with { SettingId = id, Value = target })!.Changes);
        Assert.Equal(ShellRegistryPaths.AbsentValue, change.BeforeValue);
        Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
        Assert.True((await module.RevertChangeAsync(change with
            { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue })).IsSuccess);
        var split = change.SystemLocation.LastIndexOf('\\');
        var key = change.SystemLocation[..split];
        var name = change.SystemLocation[(split + 1)..];
        Assert.False(_registry.ReadString(key, name).IsSuccess);
        Assert.False(_registry.ReadDWord(key, name).IsSuccess);
    }

    [Theory]
    [InlineData("dark-apps", "AppsUseLightTheme")]
    [InlineData("dark-windows", "SystemUsesLightTheme")]
    public void DarkMode_StagesLightBeforeStateAndDarkTarget(string id, string valueName)
    {
        var entry = HideHome with { SettingId = id, Value = "0" };
        var change = Assert.Single(new ShellSetEntryInspector(_registry).CreateChangeGroup(entry)!.Changes);
        Assert.Equal(ShellRegistryPaths.AbsentValue, change.BeforeValue);
        Assert.Equal("0", change.AfterValue);
        Assert.EndsWith("\\" + valueName, change.SystemLocation);
    }
}
