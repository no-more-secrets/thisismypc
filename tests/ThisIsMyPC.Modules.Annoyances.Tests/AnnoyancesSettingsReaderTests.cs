using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Annoyances;
using ThisIsMyPC.Modules.Annoyances.Models;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Annoyances.Tests.Fakes;

namespace ThisIsMyPC.Modules.Annoyances.Tests;

public sealed class AnnoyancesSettingsReaderTests
{
    private readonly FakeRegistryService _registry = new();

    private IReadOnlyList<AnnoyancePreference> ReadAll()
        => new AnnoyancesSettingsReader(_registry).ReadAll();

    [Fact]
    public void ReadAll_ReturnsAllPreferences()
    {
        var prefs = ReadAll();

        Assert.Equal(
            ["scoobe-nags", "welcome-experience", "app-suggestions", "windows-tips",
             "settings-suggestions", "lock-screen-ads", "silent-app-installs", "edge-shortcuts",
             "advertising-id", "activity-history",
             "game-dvr", "auto-game-mode", "hags", "sticky-keys-shortcut", "filter-keys-shortcut"],
            prefs.Select(p => p.Id));
        Assert.All(
            prefs.Take(7),
            p => Assert.Equal(AnnoyanceSection.ScoobeAndWelcome, p.Section));
        Assert.Equal(AnnoyanceSection.BingAndEdge, prefs.Single(p => p.Id == "edge-shortcuts").Section);
    }

    [Fact]
    public void MissingValues_ScanAsWindowsDefault_NotSuppressed()
    {
        var prefs = ReadAll();

        Assert.All(prefs, p =>
        {
            Assert.Equal(p.DefaultValue, p.CurrentValue);
            Assert.False(p.IsSuppressed);
        });
    }

    [Fact]
    public void SuppressedValue_ScansAsSuppressed()
    {
        _registry.SetDWord(
            AnnoyancesRegistryPaths.UserProfileEngagementKeyPath, "ScoobeSystemSettingEnabled", 0);

        var scoobe = ReadAll().Single(p => p.Id == "scoobe-nags");

        Assert.Equal("0", scoobe.CurrentValue);
        Assert.True(scoobe.IsSuppressed);
    }

    [Fact]
    public void ScoobeTargetsUserProfileEngagement_RestTargetContentDeliveryManager()
    {
        var prefs = ReadAll();

        Assert.Equal(
            AnnoyancesRegistryPaths.UserProfileEngagementKeyPath,
            prefs.Single(p => p.Id == "scoobe-nags").RegistryKeyPath);
        // The six remaining ScoobeAndWelcome prefs all live under ContentDeliveryManager
        Assert.All(
            prefs.Where(p => p.Section == AnnoyanceSection.ScoobeAndWelcome && p.Id != "scoobe-nags"),
            p => Assert.Equal(AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, p.RegistryKeyPath));
    }
}
