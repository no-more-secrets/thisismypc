using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Modules.Software.Services;

namespace ThisIsMyPC.Integration.Tests.Hardware;

/// <summary>The companion-to-catalog table in the App must name entries the bundled catalog actually has.</summary>
public sealed class CompanionCatalogMappingTests
{
    [Fact]
    public void EveryMappedCompanion_HasACatalogEntry_WithAWingetId()
    {
        foreach (var app in Enum.GetValues<CompanionApp>())
        {
            var id = HardwareCompanionActions.CatalogIdOf(app);
            if (id is null)
                continue;

            var entry = SoftwareCatalog.Entries.SingleOrDefault(e => e.Id == id);
            Assert.True(entry is not null, $"{app} maps to catalog id '{id}', which the catalog does not have.");
            Assert.False(string.IsNullOrWhiteSpace(entry!.WingetId), $"{id} has no winget id.");
        }
    }

    [Fact]
    public void TheInstallableCompanions_AreTheFourBackendsAndTheTwoMonitors()
    {
        Assert.Equal("fancontrol", HardwareCompanionActions.CatalogIdOf(CompanionApp.FanControl));
        Assert.Equal("openrgb", HardwareCompanionActions.CatalogIdOf(CompanionApp.OpenRgb));
        Assert.Equal("g-helper", HardwareCompanionActions.CatalogIdOf(CompanionApp.GHelper));
        Assert.Equal("librehardwaremonitor", HardwareCompanionActions.CatalogIdOf(CompanionApp.LibreHardwareMonitor));
        Assert.Null(HardwareCompanionActions.CatalogIdOf(CompanionApp.ArmouryCrate));
    }
}
