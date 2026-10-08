using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Annoyances;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Models;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Annoyances.Tests.Fakes;

namespace ThisIsMyPC.Modules.Annoyances.Tests;

public sealed class GamingAndAccessibilityTests
{
    private readonly FakeRegistryService _registry = new();

    private AnnoyancesSettingsReader Reader => new(_registry);

    [Fact]
    public void Section_ContainsAllSixToggles()
    {
        var prefs = Reader.ReadAll().Where(p => p.Section == AnnoyanceSection.GamingAndAccessibility).ToList();

        Assert.Equal(
            ["game-dvr", "auto-game-mode", "xbox-game-tips", "hags", "sticky-keys-shortcut", "filter-keys-shortcut"],
            prefs.Select(p => p.Id));
    }

    [Fact]
    public void GameDvr_RequiresExplorerRestart()
    {
        var pref = Reader.ReadAll().Single(p => p.Id == "game-dvr");

        Assert.Equal(RestartRequirement.ExplorerRestart, pref.RestartRequirement);
        Assert.Equal("AppCaptureEnabled", pref.RegistryValueName);
        Assert.Equal("0", AnnoyanceChangeFactory.CreateToggle(pref, suppress: true).AfterValue);
    }

    [Fact]
    public void Hags_UsesOneTwoPolarity_AndReboot()
    {
        // Missing value scans as "2" (HAGS on / driver default)
        var pref = Reader.ReadAll().Single(p => p.Id == "hags");
        Assert.Equal("2", pref.CurrentValue);
        Assert.False(pref.IsSuppressed);
        Assert.Equal(RestartRequirement.Reboot, pref.RestartRequirement);
        Assert.Equal(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", pref.RegistryKeyPath);

        var disable = AnnoyanceChangeFactory.CreateToggle(pref, suppress: true);
        Assert.Equal("1", disable.AfterValue);
        Assert.Contains("reboot", disable.DisplayName + pref.Description, StringComparison.OrdinalIgnoreCase);

        _registry.SetDWord(pref.RegistryKeyPath, "HwSchMode", 1);
        var suppressed = Reader.ReadAll().Single(p => p.Id == "hags");
        Assert.True(suppressed.IsSuppressed);
        Assert.Equal("2", AnnoyanceChangeFactory.CreateToggle(suppressed, suppress: false).AfterValue);
    }

    [Theory]
    [InlineData("sticky-keys-shortcut", @"HKCU\Control Panel\Accessibility\StickyKeys", "506", "510")]
    [InlineData("filter-keys-shortcut", @"HKCU\Control Panel\Accessibility\Keyboard Response", "122", "126")]
    public void AccessibilityFlags_AreStringValues(string id, string keyPath, string suppressed, string defaultValue)
    {
        var pref = Reader.ReadAll().Single(p => p.Id == id);

        Assert.Equal(keyPath, pref.RegistryKeyPath);
        Assert.Equal("Flags", pref.RegistryValueName);
        Assert.Equal(ChangeValueType.Registry_String, pref.ValueType);
        Assert.Equal(suppressed, pref.SuppressedValue);
        Assert.Equal(defaultValue, pref.DefaultValue);
        Assert.Equal(string.Empty, pref.CurrentValue);
        Assert.NotNull(pref.UnavailableReason); // No safe baseline for the other accessibility options.
        Assert.Equal(RestartRequirement.SignOut, pref.RestartRequirement); // Flags load at logon
    }

    [Fact]
    public async Task StickyKeysFlags_StringRoundTrip_ThroughModule()
    {
        // A customized Flags value must be captured verbatim and restored on revert
        _registry.SetString(AnnoyancesRegistryPaths.StickyKeysKeyPath, "Flags", "511");
        var module = new AnnoyancesModule(_registry);
        var pref = Reader.ReadAll().Single(p => p.Id == "sticky-keys-shortcut");

        var change = AnnoyanceChangeFactory.CreateToggle(pref, suppress: true);
        Assert.Equal("511", change.BeforeValue);

        var apply = await module.ApplyChangeAsync(change);
        Assert.True(apply.IsSuccess, apply.ErrorMessage);
        Assert.Equal("507", _registry.ReadString(AnnoyancesRegistryPaths.StickyKeysKeyPath, "Flags").Value);

        // Revert contract: swapped descriptor, apply AfterValue
        var swapped = change with
        {
            BeforeValue = change.AfterValue ?? "",
            AfterValue = change.BeforeValue,
        };
        var revert = await module.RevertChangeAsync(swapped);
        Assert.True(revert.IsSuccess, revert.ErrorMessage);
        Assert.Equal("511", _registry.ReadString(AnnoyancesRegistryPaths.StickyKeysKeyPath, "Flags").Value);
    }

    [Theory]
    [InlineData("sticky-keys-shortcut", "26", true, "26", "30")]
    [InlineData("sticky-keys-shortcut", "511", false, "507", "511")]
    [InlineData("filter-keys-shortcut", "3", true, "3", "7")]
    [InlineData("filter-keys-shortcut", "127", false, "123", "127")]
    [InlineData("sticky-keys-shortcut", "4294967295", false, "4294967291", "4294967295")]
    [InlineData("sticky-keys-shortcut", "0026", true, "26", "30")]
    public void Shortcut_OnlyChangesHotkeyBit(string id, string before, bool suppressed, string off, string on)
    {
        var key = Reader.ReadAll().Single(p => p.Id == id).RegistryKeyPath;
        _registry.SetString(key, "Flags", before);
        var pref = Reader.ReadAll().Single(p => p.Id == id);
        Assert.Null(pref.UnavailableReason);
        Assert.Equal(suppressed, pref.IsSuppressed);
        Assert.Equal(off, AnnoyanceChangeFactory.CreateToggle(pref, true).AfterValue);
        Assert.Equal(on, AnnoyanceChangeFactory.CreateToggle(pref, false).AfterValue);
        Assert.Equal(before, AnnoyanceChangeFactory.CreateToggle(pref, true).BeforeValue);
    }

    [Theory]
    [InlineData("sticky-keys-shortcut", "506", "510")]
    [InlineData("filter-keys-shortcut", "122", "126")]
    public void AllLowFlagCombinations_PreserveOtherOptions_AndAgreeWithPresets(string id, string off, string on)
    {
        var key = Reader.ReadAll().Single(p => p.Id == id).RegistryKeyPath;
        var inspector = new AnnoyancesSetEntryInspector(_registry);
        for (uint flags = 0; flags < 1024; flags++)
        {
            _registry.SetString(key, "Flags", flags.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var pref = Reader.ReadAll().Single(p => p.Id == id);
            foreach (var suppress in new[] { false, true })
            {
                var entry = new ThisIsMyPC.Core.Sets.SetEntry
                {
                    ModuleId = "Windows Annoyances", SettingId = id,
                    Value = suppress ? off : on, Description = "Shortcut",
                };
                var state = inspector.Inspect(entry)!;
                Assert.Equal(suppress == ((flags & 4) == 0), state.IsApplied);
                var change = Assert.Single(inspector.CreateChangeGroup(entry)!.Changes);
                var after = uint.Parse(change.AfterValue!, System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(flags & ~4u, after & ~4u);
                Assert.Equal(suppress ? 0u : 4u, after & 4u);
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("-1")]
    [InlineData("4294967296")]
    public void MissingOrMalformedFlags_RefuseChanges(string? value)
    {
        if (value is not null)
            _registry.SetString(AnnoyancesRegistryPaths.StickyKeysKeyPath, "Flags", value);
        var pref = Reader.ReadAll().Single(p => p.Id == "sticky-keys-shortcut");
        Assert.NotNull(pref.UnavailableReason);
        Assert.False(pref.IsSuppressed);
        Assert.Throws<InvalidOperationException>(() => AnnoyanceChangeFactory.CreateToggle(pref, true));
        var entry = new ThisIsMyPC.Core.Sets.SetEntry
        {
            ModuleId = "Windows Annoyances", SettingId = pref.Id, Value = "506", Description = "Shortcut",
        };
        Assert.Null(new AnnoyancesSetEntryInspector(_registry).Inspect(entry));
        Assert.Null(new AnnoyancesSetEntryInspector(_registry).CreateChangeGroup(entry));
    }

    [Fact]
    public void WrongRegistryType_RefusesChange()
    {
        _registry.SetDWord(AnnoyancesRegistryPaths.StickyKeysKeyPath, "Flags", 26);
        var pref = Reader.ReadAll().Single(p => p.Id == "sticky-keys-shortcut");
        Assert.NotNull(pref.UnavailableReason);
        Assert.Throws<InvalidOperationException>(() => AnnoyanceChangeFactory.CreateToggle(pref, false));
    }

    [Theory]
    [InlineData("sticky-keys-shortcut", "0026", false)]
    [InlineData("filter-keys-shortcut", "00127", true)]
    public async Task FailedBatch_RollsBackExactKeyboardOptions(string id, string before, bool suppress)
    {
        var key = Reader.ReadAll().Single(p => p.Id == id).RegistryKeyPath;
        _registry.SetString(key, "Flags", before);
        var preference = Reader.ReadAll().Single(p => p.Id == id);
        var pending = new ThisIsMyPC.Core.Services.PendingChangesService();
        var second = Reader.ReadAll().Single(p => p.Id == "advertising-id");
        _registry.SetWriteFailure(second.RegistryKeyPath, ThisIsMyPC.Core.Results.ErrorCategory.AccessDenied);
        pending.Stage(new ChangeGroup
        {
            GroupId = "keyboard-rollback", DisplayName = "Keyboard and advertising", Description = "Rollback test",
            Changes = [AnnoyanceChangeFactory.CreateToggle(preference, suppress), AnnoyanceChangeFactory.CreateToggle(second, true)],
        });
        var module = new AnnoyancesModule(_registry);
        await pending.ApplyAllAsync(module.ApplyChangeAsync, module.RevertChangeAsync);
        Assert.Equal(before, _registry.ReadString(key, "Flags").Value);
        Assert.Single(pending.PendingGroups);
        Assert.False(pending.WasApplied("keyboard-rollback"));
    }

    [Fact]
    public void Preset_ReopenRecognizesResolvedWrite_AndDetectsChangedUnrelatedOptions()
    {
        _registry.SetString(AnnoyancesRegistryPaths.StickyKeysKeyPath, "Flags", "511");
        var inspector = new AnnoyancesSetEntryInspector(_registry);
        var entry = new ThisIsMyPC.Core.Sets.SetEntry
        {
            ModuleId = "Windows Annoyances", SettingId = "sticky-keys-shortcut", Value = "506", Description = "Shortcut",
        };
        var definition = new ThisIsMyPC.Core.Sets.SetDefinition
        {
            Name = "Keyboard", Description = "Shortcut", Category = ThisIsMyPC.Core.Sets.SetCategory.TweakSet,
            Version = "1", Author = "Test", Entries = [entry], Source = ThisIsMyPC.Core.Sets.SetSource.User,
            FilePath = "keyboard.json",
        };
        var staged = inspector.CreateChangeGroup(entry)!;
        Assert.Equal("507", Assert.Single(staged.Changes).AfterValue);
        var resolver = new ThisIsMyPC.Core.Sets.SetConflictResolver([inspector],
            _ => new ThisIsMyPC.Core.Modules.ModuleAvailability(true));
        Assert.Equal(ThisIsMyPC.Core.Sets.SetEntryConflict.PendingSameValue,
            Assert.Single(resolver.Resolve(definition, [staged])).Conflict);
        _registry.SetString(AnnoyancesRegistryPaths.StickyKeysKeyPath, "Flags", "31");
        Assert.Equal(ThisIsMyPC.Core.Sets.SetEntryConflict.PendingDifferentValue,
            Assert.Single(resolver.Resolve(definition, [staged])).Conflict);
    }
}
