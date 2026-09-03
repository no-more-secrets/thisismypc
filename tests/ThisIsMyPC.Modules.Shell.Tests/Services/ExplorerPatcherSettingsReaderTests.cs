using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

/// <summary>
/// The ExplorerPatcher catalog and its live reader. The catalog is imported
/// from ExplorerPatcher's own manifest by
/// tools/import-explorerpatcher-settings.ps1.
/// </summary>
public sealed class ExplorerPatcherSettingsReaderTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly ExplorerPatcherSettingsReader _sut;

    public ExplorerPatcherSettingsReaderTests()
    {
        _sut = new ExplorerPatcherSettingsReader(_registry);
    }

    [Fact]
    public void The_catalog_is_complete_and_well_formed()
    {
        var entries = ExplorerPatcherCatalog.Entries;

        Assert.NotEmpty(entries);
        Assert.Equal(entries.Count, entries.Select(e => e.SystemLocation).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.DisplayName));
            Assert.StartsWith("HK", e.RegistryKeyPath, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(e.RegistryValueName));
            if (e.Kind == ExplorerPatcherSettingKind.Choice)
                Assert.True(e.Options.Count >= 2, $"{e.RegistryValueName} is a choice with {e.Options.Count} option(s)");
        });
        // Every tab gets some of them.
        Assert.Equal(5, entries.Select(e => e.Section).Distinct().Count());
    }

    [Fact]
    public void The_catalog_never_repeats_a_value_the_app_already_owns()
    {
        // Two rows writing one value would fight each other.
        var ours = new ExplorerSettingsReader(_registry).ReadAll()
            .Select(p => $@"{p.RegistryKeyPath}\{p.RegistryValueName}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain(ExplorerPatcherCatalog.Entries, e => ours.Contains(e.SystemLocation));
    }

    [Fact]
    public void IsInstalled_follows_the_uninstall_key()
    {
        Assert.False(_sut.IsInstalled());

        _registry.AddKey(ExplorerPatcherSettingsReader.UninstallKeyPath);

        Assert.True(_sut.IsInstalled());
    }

    [Fact]
    public void An_absent_value_reads_as_null_and_falls_back_to_the_default()
    {
        var setting = _sut.ReadAll().First(s => s.RegistryValueName == "OldTaskbar");

        Assert.Null(setting.CurrentValue);
        Assert.Equal(setting.DefaultValue, setting.EffectiveValue);
    }

    [Fact]
    public void A_live_value_is_read_back()
    {
        _registry.SetDWord(ExplorerPatcherSettingsReader.ExplorerPatcherKeyPath, "SkinMenus", 0);

        var setting = _sut.ReadAll().First(s => s.RegistryValueName == "SkinMenus");

        Assert.Equal(0, setting.CurrentValue);
        Assert.False(setting.IsOn);
    }

    [Fact]
    public void An_inverted_toggle_reads_on_when_the_value_is_zero()
    {
        var setting = ExplorerPatcherCatalog.Entries.First(s => s.Kind == ExplorerPatcherSettingKind.InvertedToggle);

        Assert.True((setting with { CurrentValue = 0 }).IsOn);
        Assert.False((setting with { CurrentValue = 1 }).IsOn);
        Assert.Equal(0, setting.ValueFor(on: true));
        Assert.Equal(1, setting.ValueFor(on: false));
    }

    [Fact]
    public void Rows_that_need_the_old_taskbar_hide_while_the_Windows_11_taskbar_is_in_use()
    {
        _registry.SetDWord(ExplorerPatcherSettingsReader.ExplorerPatcherKeyPath, "OldTaskbar", 0);
        var withWindows11Taskbar = _sut.ReadAll();

        _registry.SetDWord(ExplorerPatcherSettingsReader.ExplorerPatcherKeyPath, "OldTaskbar", 1);
        var withWindows10Taskbar = _sut.ReadAll();

        var startButton = "OrbStyle";   // section condition IsOldTaskbar
        Assert.False(withWindows11Taskbar.First(s => s.RegistryValueName == startButton).IsAvailable);
        Assert.True(withWindows10Taskbar.First(s => s.RegistryValueName == startButton).IsAvailable);
    }

    [Fact]
    public void Window_switcher_rows_hide_until_its_switcher_is_selected()
    {
        var perApp = "SwitcherIsPerApplication";   // section condition IsSWSEnabled
        Assert.False(_sut.ReadAll().First(s => s.RegistryValueName == perApp).IsAvailable);

        _registry.SetDWord(ShellRegistryPaths.ExplorerKeyPath, "AltTabSettings", 2);

        Assert.True(_sut.ReadAll().First(s => s.RegistryValueName == perApp).IsAvailable);
    }

    [Fact]
    public void A_change_records_an_absent_value_as_absent_so_undo_removes_it_again()
    {
        var setting = _sut.ReadAll().First(s => s.RegistryValueName == "SkinMenus");

        var change = ExplorerPatcherChangeFactory.Create(setting, liveValue: null, newValue: 0);

        Assert.Equal(ShellRegistryPaths.AbsentValue, change.BeforeValue);
        Assert.Equal("0", change.AfterValue);
        Assert.Contains("not set", change.BeforeDisplay, StringComparison.Ordinal);
        Assert.Equal(ChangeValueType.Registry_DWord, change.ValueType);
        Assert.Equal(setting.SystemLocation, change.SystemLocation);
    }

    [Fact]
    public void A_set_can_capture_and_replay_an_ExplorerPatcher_setting()
    {
        // The end goal is exporting a whole configuration, so a saved set has
        // to resolve these rows as well as the app's own.
        _registry.SetDWord(ExplorerPatcherSettingsReader.ExplorerPatcherKeyPath, "SkinMenus", 1);
        var inspector = new ShellSetEntryInspector(_registry);
        var entry = new Core.Sets.SetEntry
        {
            ModuleId = ExplorerPatcherChangeFactory.ModuleId,
            SettingId = ExplorerPatcherChangeFactory.SettingIdPrefix + "SkinMenus",
            Value = "0",
            Description = "Skin taskbar and tray pop-up menus",
        };

        var state = inspector.Inspect(entry);
        Assert.NotNull(state);
        Assert.Equal("1", state!.CurrentValue);
        Assert.False(state.IsApplied);

        var group = inspector.CreateChangeGroup(entry);
        var change = Assert.Single(Assert.Single([group!]).Changes);
        Assert.Equal("1", change.BeforeValue);
        Assert.Equal("0", change.AfterValue);
    }

    [Fact]
    public void A_change_from_a_live_value_carries_both_sides_and_any_restart()
    {
        var setting = ExplorerPatcherCatalog.Entries.First(s => s.RegistryValueName == "OldTaskbar") with { CurrentValue = 2 };

        var change = ExplorerPatcherChangeFactory.Create(setting, liveValue: 2, newValue: 1);

        Assert.Equal("2", change.BeforeValue);
        Assert.Equal("1", change.AfterValue);
        Assert.Equal(setting.DisplayFor(2), change.BeforeDisplay);
        Assert.Equal(setting.DisplayFor(1), change.AfterDisplay);
        Assert.Equal(RestartRequirement.ExplorerRestart, change.RestartRequirement);
        Assert.Equal(ExplorerPatcherChangeFactory.SettingIdPrefix + "OldTaskbar", change.SettingId);
    }
}
