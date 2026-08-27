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
             "settings-suggestions", "lock-screen-ads", "silent-app-installs", "edge-shortcuts"],
            prefs.Select(p => p.Id));
        Assert.All(
            prefs.Where(p => p.Id != "edge-shortcuts"),
            p => Assert.Equal(AnnoyanceSection.ScoobeAndWelcome, p.Section));
        Assert.Equal(AnnoyanceSection.BingAndEdge, prefs.Single(p => p.Id == "edge-shortcuts").Section);
        Assert.All(prefs, p => Assert.Equal(ChangeValueType.Registry_DWord, p.ValueType));
        Assert.All(prefs, p => Assert.Equal(RestartRequirement.None, p.RestartRequirement));
    }

    [Fact]
    public void MissingValues_ScanAsWindowsDefault_NotSuppressed()
    {
        var prefs = ReadAll();

        Assert.All(prefs, p =>
        {
            Assert.Equal("1", p.CurrentValue);
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
        Assert.All(
            prefs.Where(p => p.Id is not "scoobe-nags" and not "edge-shortcuts"),
            p => Assert.Equal(AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, p.RegistryKeyPath));
    }
}
