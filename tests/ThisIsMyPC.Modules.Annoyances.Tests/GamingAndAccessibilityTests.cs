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
    public void Section_ContainsAllFiveToggles()
    {
        var prefs = Reader.ReadAll().Where(p => p.Section == AnnoyanceSection.GamingAndAccessibility).ToList();

        Assert.Equal(
            ["game-dvr", "auto-game-mode", "hags", "sticky-keys-shortcut", "filter-keys-shortcut"],
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
        Assert.Equal(defaultValue, pref.CurrentValue); // missing value = Windows default
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
        Assert.Equal("506", _registry.ReadString(AnnoyancesRegistryPaths.StickyKeysKeyPath, "Flags").Value);

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
}
