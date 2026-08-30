using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Modules.Privacy;
using ThisIsMyPC.Modules.Privacy.Services;
using ThisIsMyPC.Modules.Privacy.Tests.Fakes;

namespace ThisIsMyPC.Modules.Privacy.Tests;

public sealed class PrivacyCardProviderTests
{
    private readonly FakeRegistryService _registry = new();

    private IReadOnlyList<SettingCardSource> Build()
    {
        var reader = new PrivacySettingsReader(_registry);
        return new PrivacyCardProvider(reader).BuildCards(reader.ReadAll());
    }

    [Fact]
    public void BuildCards_SixCards_GroupedBySection()
    {
        var cards = Build();

        Assert.Equal(
            ["telemetry-level", "error-reporting", "location", "app-launch-tracking",
             "inking-typing", "handwriting-data-sharing"],
            cards.Select(c => c.Model.SettingId));
        Assert.Equal(
            ["Diagnostic Data", "Permissions & Tracking", "Personalization"],
            cards.Select(c => c.Model.GroupId).Distinct());
        Assert.All(cards, c => Assert.Equal("Privacy & Telemetry", c.Model.ModuleId));
    }

    [Fact]
    public void TelemetryCard_ShowsTheEnforcedBadge_ForTheDiagTrackCompanion()
    {
        var telemetry = Build().Single(c => c.Model.SettingId == "telemetry-level");

        Assert.NotNull(telemetry.Model.Enforcement);
        Assert.Equal(EnforcementLevel.Enforced, telemetry.Model.Enforcement!.Level);
        Assert.Null(telemetry.Model.SkuRestriction);
    }

    [Fact]
    public void ProPolicyCards_CarryTheTag_SkuOnlyEnforcementRendersNoBadge()
    {
        var cards = Build();

        foreach (var id in new[] { "location", "handwriting-data-sharing" })
        {
            var card = cards.Single(c => c.Model.SettingId == id);
            Assert.Equal(WindowsSku.Pro, card.Model.SkuRestriction);
            Assert.Null(card.Model.Enforcement);
        }
    }

    [Fact]
    public void ToggleGroups_ReadLiveState_AtStageTime()
    {
        var cards = Build();
        var telemetry = cards.Single(c => c.Model.SettingId == "telemetry-level");

        // Registry changes AFTER scan; staging must capture the live before-value.
        _registry.SetDWord(PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry", 3);

        var group = telemetry.CreateToggleGroup(true);
        Assert.Equal("3", group.Changes.Single().BeforeValue);

        var inking = cards.Single(c => c.Model.SettingId == "inking-typing");
        Assert.Equal(4, inking.CreateToggleGroup(true).Changes.Count);
        Assert.False(inking.ReadCurrentState());
    }
}
