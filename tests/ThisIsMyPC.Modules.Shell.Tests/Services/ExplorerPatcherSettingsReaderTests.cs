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

    // Windows 11 23H2 with ExplorerPatcher's taskbar DLL present: every style
    // and every flyout still exists, so the machine running the tests does not
    // decide what a row offers.
    private const int Build23H2 = 22631;
    private const int Build25H2 = 26200;

    private ExplorerPatcherSettingsReader ReaderFor(int build, bool taskbarDll = true) =>
        new(_registry, build, _ => taskbarDll);

    public ExplorerPatcherSettingsReaderTests()
    {
        _sut = ReaderFor(Build23H2);
    }

    [Fact]
    public void The_catalog_is_complete_and_well_formed()
    {
        var entries = ExplorerPatcherCatalog.Entries;

        Assert.NotEmpty(entries);
        // One row per value and condition: a value ExplorerPatcher defines once
        // per Windows version appears once per version, never twice for one.
        Assert.Equal(entries.Count, entries.Select(e => e.SystemLocation + "|" + e.Condition).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(e.Description), $"{e.RegistryValueName} has no description");
            Assert.DoesNotContain("%PLACEHOLDER", e.DisplayName, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("AllocConsole")]
    [InlineData("Memcheck")]
    [InlineData("EnableSymbolDownload")]
    [InlineData("LastSectionInProperties")]
    [InlineData("PropertiesInWinX")]
    [InlineData("UpdatePreferStaging")]
    [InlineData("UpdateUseLocal")]
    public void Values_that_configure_ExplorerPatcher_itself_stay_out(string valueName)
    {
        // Its debug console, memory checks, update channel, and its own
        // settings window are not part of a machine's configuration.
        Assert.DoesNotContain(ExplorerPatcherCatalog.Entries, e => e.RegistryValueName == valueName);
    }

    [Fact]
    public void Rows_from_its_Other_and_Advanced_pages_land_on_the_tab_they_belong_to()
    {
        var byName = ExplorerPatcherCatalog.Entries.DistinctBy(e => e.RegistryValueName).ToDictionary(e => e.RegistryValueName);

        Assert.Equal(ShellSection.Taskbar, byName["ToolbarSeparators"].Section);
        Assert.Equal(ShellSection.Taskbar, byName["PinnedItemsActAsQuickLaunch"].Section);
        Assert.Equal(ShellSection.Desktop, byName["Start_PowerButtonAction"].Section);
        Assert.Equal(ShellSection.Desktop, byName["PaintDesktopVersion"].Section);
        Assert.Equal(ShellSection.General, byName["DisableWinFHotkey"].Section);
        Assert.Equal("Control Panel", byName["DoNotRedirectSystemToSettingsApp"].GroupHeading);
        Assert.Equal("System tray", byName["SkinMenus"].GroupHeading);
        Assert.Equal("Window switcher (Alt+Tab)", byName["RowHeight"].GroupHeading);
        Assert.Equal(string.Empty, byName["OldTaskbar"].GroupHeading);

        // Within a group the manifest's order holds, toggles and choices interleaved,
        // so the switcher style comes first and its toggles sit before its choices.
        var order = ExplorerPatcherCatalog.Entries.Select(e => e.RegistryValueName).ToList();
        Assert.True(order.IndexOf("AltTabSettings") < order.IndexOf("IncludeWallpaper"));
        Assert.True(order.IndexOf("IncludeWallpaper") < order.IndexOf("Theme"));
        Assert.True(order.IndexOf("OldTaskbar") < order.IndexOf("SkinMenus"));
    }

    [Fact]
    public void A_value_defined_once_per_Windows_version_reads_as_one_available_row()
    {
        // The language switcher has a pre-22H2 and a 22H2+ option list; the
        // build decides which one applies.
        var variants = _sut.ReadAll().Where(s => s.RegistryValueName == "IMEStyle").ToList();

        Assert.Equal(2, variants.Count);
        var available = Assert.Single(variants, v => v.IsAvailable);
        Assert.Equal("IsWindows11Version22H2OrHigher", available.Condition);
    }

    [Fact]
    public void The_stock_Windows_10_taskbar_is_not_offered_on_builds_that_no_longer_have_one()
    {
        // Sam, 25H2: "The non-explorerpatcher one does nothing." Microsoft
        // removed that taskbar from explorer.exe at build 26002, and
        // ExplorerPatcher's own window drops the option there (GUI.c).
        var on23H2 = ReaderFor(Build23H2).ReadAll().First(s => s.RegistryValueName == "OldTaskbar");
        var on25H2 = ReaderFor(Build25H2).ReadAll().First(s => s.RegistryValueName == "OldTaskbar");

        Assert.Equal([0, 1, 2], on23H2.Options.Select(o => o.Value));
        Assert.Equal([0, 2], on25H2.Options.Select(o => o.Value));
    }

    [Fact]
    public void ExplorerPatchers_own_taskbar_is_not_offered_without_its_DLL_for_this_build()
    {
        var setting = ReaderFor(Build25H2, taskbarDll: false).ReadAll().First(s => s.RegistryValueName == "OldTaskbar");

        Assert.Equal([0], setting.Options.Select(o => o.Value));
    }

    [Fact]
    public void A_taskbar_style_whose_files_are_gone_reads_as_the_one_ExplorerPatcher_falls_back_to()
    {
        // Value 1 on 25H2 is treated as 0 by ExplorerPatcher (utility.h
        // AdjustTaskbarStyleValue), so the row shows Windows 11 and the rows
        // that need a Windows 10 taskbar stay hidden. The raw value is kept
        // for undo.
        _registry.SetDWord(ExplorerPatcherSettingsReader.ExplorerPatcherKeyPath, "OldTaskbar", 1);
        var reader = ReaderFor(Build25H2);
        var all = reader.ReadAll();

        var style = all.First(s => s.RegistryValueName == "OldTaskbar");
        Assert.Equal(1, style.CurrentValue);
        Assert.Equal(0, style.AdjustedValue);
        Assert.Equal(0, style.EffectiveValue);
        Assert.False(all.First(s => s.RegistryValueName == "OrbStyle").IsAvailable);
        Assert.Equal(0, reader.AdjustTaskbarStyle(1));
        Assert.Equal(2, reader.AdjustTaskbarStyle(2));

        // On 23H2 the same value is what it says.
        Assert.Null(ReaderFor(Build23H2).ReadAll().First(s => s.RegistryValueName == "OldTaskbar").AdjustedValue);
    }

    [Fact]
    public void The_Windows_8_network_flyout_is_not_offered_where_Windows_removed_it()
    {
        _registry.SetDWord(ExplorerPatcherSettingsReader.ExplorerPatcherKeyPath, "OldTaskbar", 2);

        var on23H2 = ReaderFor(Build23H2).ReadAll().First(s => s.RegistryValueName == "ReplaceVan");
        var on25H2 = ReaderFor(Build25H2).ReadAll().First(s => s.RegistryValueName == "ReplaceVan");

        Assert.Contains(on23H2.Options, o => o.Value == 2);
        Assert.DoesNotContain(on25H2.Options, o => o.Value == 2);
    }

    [Fact]
    public void The_taskbar_DLL_is_looked_for_under_ExplorerPatchers_install_folder()
    {
        Assert.EndsWith(@"ExplorerPatcher\ep_taskbar.ge.dll", ReaderFor(Build25H2).TaskbarDllPath(), StringComparison.Ordinal);
        Assert.EndsWith(@"ExplorerPatcher\ep_taskbar.ni.dll", ReaderFor(Build23H2).TaskbarDllPath(), StringComparison.Ordinal);
        Assert.Null(ReaderFor(10240).TaskbarDllPath());
    }

    [Fact]
    public void A_set_resolves_the_variant_whose_condition_holds()
    {
        // Show desktop button is a three-way choice on the Windows 10 taskbar
        // and a plain switch on the Windows 11 one; the same set entry has to
        // land on whichever is in force.
        var inspector = new ShellSetEntryInspector(_registry, ReaderFor(Build23H2));
        var entry = new Core.Sets.SetEntry
        {
            ModuleId = ExplorerPatcherChangeFactory.ModuleId,
            SettingId = ExplorerPatcherChangeFactory.SettingIdPrefix + "TaskbarSD",
            Value = "0",
            Description = "Show desktop button",
        };

        _registry.SetDWord(ExplorerPatcherSettingsReader.ExplorerPatcherKeyPath, "OldTaskbar", 0);
        var onWindows11Taskbar = Assert.Single(inspector.CreateChangeGroup(entry)!.Changes);
        Assert.Equal("Off", onWindows11Taskbar.AfterDisplay);

        _registry.SetDWord(ExplorerPatcherSettingsReader.ExplorerPatcherKeyPath, "OldTaskbar", 1);
        var onWindows10Taskbar = Assert.Single(inspector.CreateChangeGroup(entry)!.Changes);
        Assert.Equal("Disabled", onWindows10Taskbar.AfterDisplay);
    }

    [Fact]
    public void The_catalog_names_the_ExplorerPatcher_release_it_came_from()
    {
        // The pin is the point: these definitions match one release, and the
        // page says so when a different one is installed.
        Assert.Matches(@"^\d+(\.\d+)+$", ExplorerPatcherCatalog.Version);
    }

    [Fact]
    public void The_installed_version_comes_from_ExplorerPatchers_own_uninstall_entry()
    {
        Assert.Equal(string.Empty, _sut.InstalledVersion());

        _registry.SetString(ExplorerPatcherSettingsReader.UninstallKeyPath, "DisplayVersion", "26100.8457.70.3");

        Assert.Equal("26100.8457.70.3", _sut.InstalledVersion());
    }

    [Theory]
    [InlineData("26100.8457.70.3", "26100.8457.70.3", false)]
    [InlineData("26100.8457.70.4", "26100.8457.70.3", true)]
    [InlineData("", "26100.8457.70.3", false)]
    public void A_version_that_does_not_match_the_pin_is_reported(string installed, string catalog, bool differs)
    {
        var scan = new ShellScanData(
            ExplorerPreferences: [],
            Taskbar: new TaskbarSettings(1, true, false, false),
            ExplorerPatcherInstalled: true,
            ExplorerPatcherVersion: installed,
            ExplorerPatcherCatalogVersion: catalog);

        Assert.Equal(differs, scan.ExplorerPatcherVersionDiffers);
    }

    [Fact]
    public void ExplorerPatchers_own_update_policy_is_one_of_the_rows()
    {
        // Turning its self-updater off is what keeps the installed version on
        // the release these definitions were built from.
        var policy = ExplorerPatcherCatalog.Entries.First(s => s.RegistryValueName == "UpdatePolicy");

        Assert.Equal(ExplorerPatcherSettingKind.Choice, policy.Kind);
        Assert.Contains(policy.Options, o => o.DisplayName.Contains("Do not check", StringComparison.Ordinal));
        Assert.Equal("Check for updates", policy.DisplayName);
        Assert.Equal("Updates", policy.GroupHeading);
        Assert.Contains("ExplorerPatcher", policy.Description, StringComparison.Ordinal);
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
        var inspector = new ShellSetEntryInspector(_registry, ReaderFor(Build23H2));
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
