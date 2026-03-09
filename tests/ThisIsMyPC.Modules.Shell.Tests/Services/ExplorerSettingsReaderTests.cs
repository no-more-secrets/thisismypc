using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

public sealed class ExplorerSettingsReaderTests
{
    private const string AdvancedKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ExplorerKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer";

private readonly FakeRegistryService _registry = new();
    private readonly ExplorerSettingsReader _sut;

    public ExplorerSettingsReaderTests()
    {
        _sut = new ExplorerSettingsReader(_registry);
    }

    [Fact]
    public void ReadAll_returns_all_nine_preferences()
    {
        SetDefaults();
        var prefs = _sut.ReadAll();
        Assert.Equal(9, prefs.Count);
    }

    [Fact]
    public void HiddenFiles_enabled_when_Hidden_is_1()
    {
        _registry.SetDWord(AdvancedKeyPath, "Hidden", 1);
        SetRemainingDefaults();

        var prefs = _sut.ReadAll();
        var hidden = prefs.First(p => p.Id == "hidden-files");

        Assert.True(hidden.IsEnabled);
        Assert.Equal("1", hidden.CurrentValue);
        Assert.Equal("1", hidden.EnabledValue);
        Assert.Equal("2", hidden.DisabledValue);
        Assert.Equal(ChangeValueType.Registry_DWord, hidden.ValueType);
        Assert.Equal(RestartRequirement.ExplorerRefresh, hidden.RestartRequirement);
    }

    [Fact]
    public void HiddenFiles_disabled_when_Hidden_is_2()
    {
        _registry.SetDWord(AdvancedKeyPath, "Hidden", 2);
        SetRemainingDefaults();

        var prefs = _sut.ReadAll();
        var hidden = prefs.First(p => p.Id == "hidden-files");

        Assert.False(hidden.IsEnabled);
        Assert.Equal("2", hidden.CurrentValue);
    }

    [Fact]
    public void FileExtensions_enabled_when_HideFileExt_is_0()
    {
        SetDefaults();
        _registry.SetDWord(AdvancedKeyPath, "HideFileExt", 0);

        var prefs = _sut.ReadAll();
        var ext = prefs.First(p => p.Id == "file-extensions");

        Assert.True(ext.IsEnabled);
        Assert.Equal("0", ext.EnabledValue);
        Assert.Equal("1", ext.DisabledValue);
    }

    [Fact]
    public void CorrectRegistryPaths_are_set()
    {
        SetDefaults();
        var prefs = _sut.ReadAll();

        var hidden = prefs.First(p => p.Id == "hidden-files");
        Assert.Equal(AdvancedKeyPath, hidden.RegistryKeyPath);
        Assert.Equal("Hidden", hidden.RegistryValueName);

        var launchTo = prefs.First(p => p.Id == "launch-to");
        Assert.Equal(ExplorerKeyPath, launchTo.RegistryKeyPath);
        Assert.Equal("LaunchTo", launchTo.RegistryValueName);
    }

    [Fact]
    public void Missing_registry_value_uses_default()
    {
        // Don't set any values — reader should use defaults
        var prefs = _sut.ReadAll();

        var hidden = prefs.First(p => p.Id == "hidden-files");
        Assert.Equal("2", hidden.CurrentValue); // default is "2" (disabled)
        Assert.False(hidden.IsEnabled);
    }

    // --- Navigation Pane Preferences (Task 2) ---

    [Fact]
    public void NavPaneShowAllFolders_enabled_when_value_is_1()
    {
        _registry.SetDWord(AdvancedKeyPath, "NavPaneShowAllFolders", 1);
        var prefs = _sut.ReadAll();
        var pref = prefs.First(p => p.Id == "nav-pane-show-all-folders");

        Assert.True(pref.IsEnabled);
        Assert.Equal("1", pref.CurrentValue);
        Assert.Equal(RestartRequirement.ExplorerRestart, pref.RestartRequirement);
    }

    [Fact]
    public void NavPaneShowAllFolders_disabled_when_value_is_0()
    {
        _registry.SetDWord(AdvancedKeyPath, "NavPaneShowAllFolders", 0);
        var prefs = _sut.ReadAll();
        var pref = prefs.First(p => p.Id == "nav-pane-show-all-folders");

        Assert.False(pref.IsEnabled);
        Assert.Equal("0", pref.CurrentValue);
    }

    [Fact]
    public void NavPaneExpandToCurrentFolder_enabled_when_value_is_1()
    {
        _registry.SetDWord(AdvancedKeyPath, "NavPaneExpandToCurrentFolder", 1);
        var prefs = _sut.ReadAll();
        var pref = prefs.First(p => p.Id == "nav-pane-expand-to-current");

        Assert.True(pref.IsEnabled);
        Assert.Equal("1", pref.CurrentValue);
        Assert.Equal(RestartRequirement.ExplorerRestart, pref.RestartRequirement);
    }

    [Fact]
    public void NavPaneExpandToCurrentFolder_disabled_when_value_is_0()
    {
        _registry.SetDWord(AdvancedKeyPath, "NavPaneExpandToCurrentFolder", 0);
        var prefs = _sut.ReadAll();
        var pref = prefs.First(p => p.Id == "nav-pane-expand-to-current");

        Assert.False(pref.IsEnabled);
        Assert.Equal("0", pref.CurrentValue);
    }

    // --- Compact View (Task 3) ---

    [Fact]
    public void UseCompactMode_enabled_when_value_is_1()
    {
        _registry.SetDWord(AdvancedKeyPath, "UseCompactMode", 1);
        var prefs = _sut.ReadAll();
        var pref = prefs.First(p => p.Id == "compact-view");

        Assert.True(pref.IsEnabled);
        Assert.Equal("1", pref.CurrentValue);
        Assert.Equal(RestartRequirement.None, pref.RestartRequirement);
    }

    [Fact]
    public void UseCompactMode_disabled_when_value_is_0()
    {
        _registry.SetDWord(AdvancedKeyPath, "UseCompactMode", 0);
        var prefs = _sut.ReadAll();
        var pref = prefs.First(p => p.Id == "compact-view");

        Assert.False(pref.IsEnabled);
        Assert.Equal("0", pref.CurrentValue);
    }

    [Fact]
    public void UseCompactMode_defaults_to_disabled()
    {
        // No value set — should default to 0 (normal spacing)
        var prefs = _sut.ReadAll();
        var pref = prefs.First(p => p.Id == "compact-view");

        Assert.False(pref.IsEnabled);
        Assert.Equal("0", pref.CurrentValue);
    }

    private void SetDefaults()
    {
        _registry.SetDWord(AdvancedKeyPath, "Hidden", 2);
        _registry.SetDWord(AdvancedKeyPath, "HideFileExt", 1);
        _registry.SetDWord(AdvancedKeyPath, "ShowSuperHidden", 0);
        _registry.SetDWord(AdvancedKeyPath, "SeparateProcess", 0);
        _registry.SetDWord(AdvancedKeyPath, "ShowSyncProviderNotifications", 1);
        _registry.SetDWord(ExplorerKeyPath, "LaunchTo", 2);
    }

    private void SetRemainingDefaults()
    {
        // Set everything except what's already been set by the specific test
        if (_registry.ReadDWord(AdvancedKeyPath, "HideFileExt").ErrorCategory is not null)
            _registry.SetDWord(AdvancedKeyPath, "HideFileExt", 1);
        if (_registry.ReadDWord(AdvancedKeyPath, "ShowSuperHidden").ErrorCategory is not null)
            _registry.SetDWord(AdvancedKeyPath, "ShowSuperHidden", 0);
        if (_registry.ReadDWord(AdvancedKeyPath, "SeparateProcess").ErrorCategory is not null)
            _registry.SetDWord(AdvancedKeyPath, "SeparateProcess", 0);
        if (_registry.ReadDWord(AdvancedKeyPath, "ShowSyncProviderNotifications").ErrorCategory is not null)
            _registry.SetDWord(AdvancedKeyPath, "ShowSyncProviderNotifications", 1);
        if (_registry.ReadDWord(ExplorerKeyPath, "LaunchTo").ErrorCategory is not null)
            _registry.SetDWord(ExplorerKeyPath, "LaunchTo", 2);
    }
}
