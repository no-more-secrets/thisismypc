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
             "settings-suggestions", "lock-screen-images", "spotlight-collection-desktop",
             "consumer-features", "silent-app-installs", "edge-shortcuts",
             "dynamic-search-box", "advertising-id", "activity-history",
             "tailored-experiences", "language-list-access", "feedback-frequency",
             "game-dvr", "auto-game-mode", "xbox-game-tips", "hags",
             "sticky-keys-shortcut", "filter-keys-shortcut",
             "copilot-button", "edge-sidebar"],
            prefs.Select(p => p.Id));
        Assert.Equal(9, prefs.Count(p => p.Section == AnnoyanceSection.ScoobeAndWelcome));
        Assert.Equal(AnnoyanceSection.BingAndEdge, prefs.Single(p => p.Id == "edge-shortcuts").Section);
        Assert.Equal(AnnoyanceSection.BingAndEdge, prefs.Single(p => p.Id == "dynamic-search-box").Section);
        // The original suppression prefs keep the simple shape: DWORD 0/1, no restart
        Assert.All(
            prefs.Where(p => p.Section is AnnoyanceSection.ScoobeAndWelcome or AnnoyanceSection.BingAndEdge),
            p =>
            {
                Assert.Equal(ChangeValueType.Registry_DWord, p.ValueType);
                Assert.Equal(RestartRequirement.None, p.RestartRequirement);
            });
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
    public void LockScreenAds_IsAnAtomicPairOfCdmValues()
    {
        var prefs = new AnnoyancesSettingsReader(_registry).ReadLockScreenAds();

        Assert.Equal(
            ["RotatingLockScreenOverlayEnabled", "SubscribedContent-338387Enabled"],
            prefs.Select(p => p.RegistryValueName));
        Assert.All(prefs, p =>
        {
            Assert.Equal("lock-screen-ads", p.Id);
            Assert.Equal(AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, p.RegistryKeyPath);
            Assert.Equal("0", p.SuppressedValue);
        });
    }

    [Fact]
    public void PreinstalledApps_IsAnAtomicTrioOfCdmValues()
    {
        var prefs = new AnnoyancesSettingsReader(_registry).ReadPreinstalledApps();

        Assert.Equal(
            ["OemPreInstalledAppsEnabled", "PreInstalledAppsEnabled", "SoftLandingEnabled"],
            prefs.Select(p => p.RegistryValueName));
        Assert.All(prefs, p =>
        {
            Assert.Equal("preinstalled-apps", p.Id);
            Assert.Equal(AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, p.RegistryKeyPath);
            Assert.Equal("0", p.SuppressedValue);
        });
    }

    [Fact]
    public void EdgeDebloat_IsAnAtomicTrioOfEdgePolicies()
    {
        var prefs = new AnnoyancesSettingsReader(_registry).ReadEdgeDebloat();

        Assert.Equal(
            ["EdgeShoppingAssistantEnabled", "ShowMicrosoftRewards", "PersonalizationReportingEnabled"],
            prefs.Select(p => p.RegistryValueName));
        Assert.All(prefs, p =>
        {
            Assert.Equal("edge-debloat", p.Id);
            Assert.Equal(AnnoyancesRegistryPaths.EdgePoliciesKeyPath, p.RegistryKeyPath);
            Assert.Equal("0", p.SuppressedValue);
        });
    }

    [Fact]
    public void ScoobeTargetsUserProfileEngagement_RestTargetContentDeliveryManager()
    {
        var prefs = ReadAll();

        Assert.Equal(
            AnnoyancesRegistryPaths.UserProfileEngagementKeyPath,
            prefs.Single(p => p.Id == "scoobe-nags").RegistryKeyPath);
        // The remaining ScoobeAndWelcome prefs all live under ContentDeliveryManager,
        // except the CloudContent Spotlight and consumer-features policies.
        Assert.Equal(
            AnnoyancesRegistryPaths.CloudContentUserPoliciesKeyPath,
            prefs.Single(p => p.Id == "spotlight-collection-desktop").RegistryKeyPath);
        Assert.Equal(
            AnnoyancesRegistryPaths.CloudContentMachinePoliciesKeyPath,
            prefs.Single(p => p.Id == "consumer-features").RegistryKeyPath);
        Assert.All(
            prefs.Where(p => p.Section == AnnoyanceSection.ScoobeAndWelcome
                && p.Id is not "scoobe-nags" and not "spotlight-collection-desktop" and not "consumer-features"),
            p => Assert.Equal(AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, p.RegistryKeyPath));
    }
}
