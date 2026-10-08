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
        _registry.SetString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Professional");
        return new PrivacyCardProvider(reader, new Core.Services.CapabilityDetector(_registry)).BuildCards(reader.ReadAll());
    }

    [Fact]
    public void BuildCards_EightCards_GroupedBySection()
    {
        var cards = Build();

        Assert.Equal(
            ["telemetry-level", "error-reporting", "location", "app-launch-tracking",
             "cross-device-clipboard", "online-speech",
             "inking-typing", "handwriting-data-sharing"],
            cards.Select(c => c.Model.SettingId));
        Assert.Equal(
            ["Diagnostic Data", "Permissions & Tracking", "Personalization"],
            cards.Select(c => c.Model.GroupId).Distinct());
        Assert.All(cards, c => Assert.Equal("Privacy & Telemetry", c.Model.ModuleId));
    }

    [Fact]
    public void TelemetryCard_UsesPolicyChoicesWithoutChangingTheService()
    {
        var telemetry = Build().Single(c => c.Model.SettingId == "telemetry-level");

        Assert.Null(telemetry.Model.Enforcement);
        Assert.Equal(SettingControlType.Dropdown, telemetry.Model.ControlType);
        Assert.Equal(WindowsSku.Pro, telemetry.Model.SkuRestriction);
        Assert.Equal(new[] { "", "1", "3" }, telemetry.Model.AvailableOptions!.Select(o => o.Value));
    }

    [Fact]
    public void ProPolicyCards_CarryTheTag_SkuOnlyEnforcementRendersNoBadge()
    {
        var cards = Build();

        foreach (var id in new[] { "location", "cross-device-clipboard", "handwriting-data-sharing" })
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

        var group = telemetry.CreateChoiceGroup!("1");
        Assert.Equal("3", group.Changes.Single().BeforeValue);

        var inking = cards.Single(c => c.Model.SettingId == "inking-typing");
        Assert.Equal(4, inking.CreateToggleGroup(true).Changes.Count);
        Assert.False(inking.ReadCurrentState());
    }
}
