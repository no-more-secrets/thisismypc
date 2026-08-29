using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Annoyances.Tests.Fakes;

namespace ThisIsMyPC.Modules.Annoyances.Tests;

/// <summary>
/// Story 26-9: Home-edition tags on the policies whose official Policy CSP edition
/// tables exclude Home (docs/research/sku-restriction-audit.md). Informational only;
/// attached on the suppress direction (26-4 rule).
/// </summary>
public class SkuRestrictionTagTests
{
    private static AnnoyancesSettingsReader Reader() => new(new FakeRegistryService());

    [Fact]
    public void CopilotPolicy_CarriesHomeTag_OnSuppressOnly()
    {
        var prefs = Reader().ReadCopilotPolicy();

        var suppress = AnnoyanceChangeFactory.CreateCopilotPolicyToggle(prefs, suppress: true);
        var restore = AnnoyanceChangeFactory.CreateCopilotPolicyToggle(prefs, suppress: false);

        Assert.All(suppress.Changes, c => Assert.Equal(WindowsSku.Home, c.Enforcement!.SkuRestriction));
        Assert.All(restore.Changes, c => Assert.Null(c.Enforcement));
    }

    [Fact]
    public void RecallPolicy_CarriesHomeTag_OnSuppressOnly()
    {
        var prefs = Reader().ReadRecall();

        var suppress = AnnoyanceChangeFactory.CreateRecallPolicyToggle(prefs, suppress: true, "d");
        var restore = AnnoyanceChangeFactory.CreateRecallPolicyToggle(prefs, suppress: false, "d");

        Assert.All(suppress.Changes, c => Assert.Equal(WindowsSku.Home, c.Enforcement!.SkuRestriction));
        Assert.All(restore.Changes, c => Assert.Null(c.Enforcement));
    }

    [Fact]
    public void ActivityHistory_CarriesHomeTag_OnSuppressOnly()
    {
        var pref = Reader().ReadAll().Single(p => p.Id == "activity-history");

        var suppress = AnnoyanceChangeFactory.CreateToggle(pref, suppress: true);
        var restore = AnnoyanceChangeFactory.CreateToggle(pref, suppress: false);

        Assert.NotNull(suppress.Enforcement);
        Assert.Equal(WindowsSku.Home, suppress.Enforcement!.SkuRestriction);
        Assert.Null(suppress.Enforcement.ReversionVectors); // SKU-only, no drift claim
        Assert.Null(restore.Enforcement);
    }

    [Fact]
    public void OtherSingles_StayUntagged()
    {
        // bing-search/edge stay untagged deliberately (field evidence says the HKCU
        // writes are honored on Home); HKCU prefs are edition-independent.
        foreach (var pref in Reader().ReadAll().Where(p => p.Id != "activity-history"))
        {
            var change = AnnoyanceChangeFactory.CreateToggle(pref, suppress: true);
            Assert.Null(change.Enforcement?.SkuRestriction);
        }
    }

    [Fact]
    public void Cards_SurfaceTheHomeTag_AndSkuOnlyEnforcementRendersNoBadge()
    {
        var reader = Reader();
        var provider = new AnnoyancesCardProvider(reader);
        var cards = provider.BuildCards(new Models.AnnoyancesScanData(
            reader.ReadAll(),
            reader.ReadBingSearch(),
            reader.ReadSettingsSuggestedContent(),
            reader.ReadCopilotPolicy(),
            reader.ReadRecall(),
            reader.ReadLockScreenAds(),
            reader.ReadPreinstalledApps()));

        var copilot = cards.Single(c => c.Model.SettingId == "copilot");
        var recall = cards.Single(c => c.Model.SettingId == "recall");
        var activity = cards.Single(c => c.Model.SettingId == "activity-history");
        var scoobe = cards.Single(c => c.Model.SettingId == "scoobe-nags");

        Assert.Equal(WindowsSku.Home, copilot.Model.SkuRestriction);
        Assert.Equal(WindowsSku.Home, recall.Model.SkuRestriction);
        Assert.Equal(WindowsSku.Home, activity.Model.SkuRestriction);
        Assert.Null(scoobe.Model.SkuRestriction);

        // SKU-only enforcement (recall, activity-history) must not produce a
        // "known to revert" badge; copilot keeps its drift-vector profile.
        Assert.Null(recall.Model.Enforcement);
        Assert.Null(activity.Model.Enforcement);
        Assert.NotNull(copilot.Model.Enforcement);
    }
}
