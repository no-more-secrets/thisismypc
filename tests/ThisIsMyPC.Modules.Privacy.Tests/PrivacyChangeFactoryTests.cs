using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Modules.Privacy;
using ThisIsMyPC.Modules.Privacy.Changes;
using ThisIsMyPC.Modules.Privacy.Services;
using ThisIsMyPC.Modules.Privacy.Tests.Fakes;

namespace ThisIsMyPC.Modules.Privacy.Tests;

public sealed class PrivacyChangeFactoryTests
{
    private readonly FakeRegistryService _registry = new();

    private PrivacySettingsReader Reader => new(_registry);

    [Fact]
    public void TelemetryLevel_CarriesDiagTrackCompanion_OnConfigureOnly()
    {
        var pref = Reader.ReadSingles().Single(p => p.Id == "telemetry-level");

        var configure = PrivacyChangeFactory.CreateToggle(pref, configure: true);
        Assert.NotNull(configure.Enforcement);
        Assert.Equal(["DiagTrack"], configure.Enforcement!.CompanionServices);
        Assert.Null(configure.Enforcement.SkuRestriction); // AllowTelemetry supports Home

        var restore = PrivacyChangeFactory.CreateToggle(pref, configure: false);
        Assert.Null(restore.Enforcement);
    }

    [Fact]
    public void LocationAndHandwriting_CarryTheProMinimumTierTag()
    {
        var prefs = Reader.ReadSingles();

        foreach (var id in new[] { "location", "handwriting-data-sharing" })
        {
            var configure = PrivacyChangeFactory.CreateToggle(prefs.Single(p => p.Id == id), configure: true);
            Assert.Equal(WindowsSku.Pro, configure.Enforcement?.SkuRestriction);
        }
    }

    [Fact]
    public void PolicyRestore_HasEmptyAfterValue_DeleteConvention()
    {
        var pref = Reader.ReadSingles().Single(p => p.Id == "error-reporting");

        var restore = PrivacyChangeFactory.CreateToggle(pref, configure: false);

        Assert.Equal("", restore.AfterValue);
        Assert.Equal("", restore.BeforeValue); // absent policy scans as ""
    }

    [Fact]
    public void AppLaunchTracking_RestoreWritesTheWindowsDefaultBack()
    {
        var pref = Reader.ReadSingles().Single(p => p.Id == "app-launch-tracking");

        var restore = PrivacyChangeFactory.CreateToggle(pref, configure: false);

        Assert.Equal("1", restore.AfterValue);
    }

    [Fact]
    public void InkingTypingGroup_Restore_DeletesAllFourValues_WithNoEnforcement()
    {
        var group = PrivacyChangeFactory.CreateInkingTypingGroup(
            Reader.ReadInkingTyping(), configure: false, "d");

        Assert.Equal(4, group.Changes.Count);
        Assert.All(group.Changes, c => Assert.Equal("", c.AfterValue));
        Assert.All(group.Changes, c => Assert.Null(c.Enforcement));
    }

    [Fact]
    public void InkingTypingGroup_IsAtomic_WithSharedSettingId_AndLiveBeforeValues()
    {
        _registry.SetDWord(PrivacyRegistryPaths.TrainedDataStoreKeyPath, "HarvestContacts", 1);

        var group = PrivacyChangeFactory.CreateInkingTypingGroup(
            Reader.ReadInkingTyping(), configure: true, "d");

        Assert.Equal(4, group.Changes.Count);
        Assert.All(group.Changes, c => Assert.Equal("inking-typing", c.SettingId));
        var harvest = group.Changes.Single(c => c.SystemLocation.EndsWith("HarvestContacts", StringComparison.Ordinal));
        Assert.Equal("1", harvest.BeforeValue);
        Assert.Equal("0", harvest.AfterValue);
    }
}
