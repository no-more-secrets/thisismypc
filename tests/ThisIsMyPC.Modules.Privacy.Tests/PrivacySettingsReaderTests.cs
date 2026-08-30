using ThisIsMyPC.Modules.Privacy;
using ThisIsMyPC.Modules.Privacy.Models;
using ThisIsMyPC.Modules.Privacy.Services;
using ThisIsMyPC.Modules.Privacy.Tests.Fakes;

namespace ThisIsMyPC.Modules.Privacy.Tests;

public sealed class PrivacySettingsReaderTests
{
    private readonly FakeRegistryService _registry = new();

    private PrivacySettingsReader Reader => new(_registry);

    [Fact]
    public void ReadSingles_ReturnsTheCatalog_InDisplayOrder()
    {
        var prefs = Reader.ReadSingles();

        Assert.Equal(
            ["telemetry-level", "error-reporting", "location", "app-launch-tracking", "handwriting-data-sharing"],
            prefs.Select(p => p.Id));
        Assert.Equal(prefs.Count, prefs.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void PolicySingles_DefaultToNotConfigured_WithDeleteRestore()
    {
        // Policies default to "no value at all": DefaultValue is empty (delete to restore)
        var prefs = Reader.ReadSingles();
        foreach (var id in new[] { "telemetry-level", "error-reporting", "location", "handwriting-data-sharing" })
        {
            var pref = prefs.Single(p => p.Id == id);
            Assert.Equal("", pref.DefaultValue);
            Assert.Equal("", pref.CurrentValue);
            Assert.False(pref.IsConfigured);
        }
    }

    [Fact]
    public void AppLaunchTracking_HasARealDefault_WriteBackRestore()
    {
        var pref = Reader.ReadSingles().Single(p => p.Id == "app-launch-tracking");

        Assert.Equal("0", pref.ConfiguredValue);
        Assert.Equal("1", pref.DefaultValue);
        Assert.Equal("1", pref.CurrentValue); // absent scans as the Windows default
        Assert.False(pref.IsConfigured);
    }

    [Fact]
    public void TelemetryLevel_ReadsConfiguredState()
    {
        _registry.SetDWord(PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry", 1);

        var pref = Reader.ReadSingles().Single(p => p.Id == "telemetry-level");

        Assert.Equal("1", pref.CurrentValue);
        Assert.True(pref.IsConfigured);
    }

    [Fact]
    public void InkingTyping_IsAnAtomicQuartet_WithMixedPolarity()
    {
        var prefs = Reader.ReadInkingTyping();

        Assert.Equal(
            ["RestrictImplicitInkCollection", "RestrictImplicitTextCollection", "HarvestContacts", "AcceptedPrivacyPolicy"],
            prefs.Select(p => p.RegistryValueName));
        Assert.All(prefs, p => Assert.Equal("inking-typing", p.Id));
        // Mixed polarity: the restrict values configure to 1, the collection values to 0.
        // All restore by deletion — writing consent values back would fabricate consent.
        Assert.Equal(["1", "1", "0", "0"], prefs.Select(p => p.ConfiguredValue));
        Assert.All(prefs, p => Assert.Equal("", p.DefaultValue));
    }

    [Fact]
    public void ReadAll_BundlesSinglesAndTheGroup()
    {
        var scan = Reader.ReadAll();

        Assert.Equal(5, scan.Preferences.Count);
        Assert.Equal(4, scan.InkingTyping.Count);
    }
}
