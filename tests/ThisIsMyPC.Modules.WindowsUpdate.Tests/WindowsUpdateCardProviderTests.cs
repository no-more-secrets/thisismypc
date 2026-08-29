using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Modules.WindowsUpdate;
using ThisIsMyPC.Modules.WindowsUpdate.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Tests.Fakes;

namespace ThisIsMyPC.Modules.WindowsUpdate.Tests;

public class WindowsUpdateCardProviderTests
{
    [Fact]
    public void BuildCards_WithDisplayVersion_EightCards_VersionPinFirst()
    {
        var registry = new FakeRegistryService();
        registry.SetString(WindowsUpdateRegistryPaths.CurrentVersionKeyPath, "DisplayVersion", "24H2");
        var reader = new WindowsUpdateSettingsReader(registry);
        var provider = new WindowsUpdateCardProvider(reader);

        var cards = provider.BuildCards(reader.ReadAll());

        Assert.Equal(
            ["version-pin", "auto-update-mode", "no-auto-reboot", "exclude-drivers", "delivery-optimization",
             "restart-notifications", "active-hours-manual", "continuous-innovation"],
            cards.Select(c => c.Model.SettingId));
    }

    [Fact]
    public void BuildCards_NoDisplayVersion_OmitsTheVersionPinCard()
    {
        var reader = new WindowsUpdateSettingsReader(new FakeRegistryService());
        var provider = new WindowsUpdateCardProvider(reader);

        var cards = provider.BuildCards(reader.ReadAll());

        Assert.DoesNotContain("version-pin", cards.Select(c => c.Model.SettingId));
        Assert.Equal(7, cards.Count);
    }

    [Fact]
    public void GPCacheCards_ShowTheEnforcedBadge_DeliveryOptimizationShowsNone()
    {
        var registry = new FakeRegistryService();
        registry.SetString(WindowsUpdateRegistryPaths.CurrentVersionKeyPath, "DisplayVersion", "24H2");
        var reader = new WindowsUpdateSettingsReader(registry);
        var cards = new WindowsUpdateCardProvider(reader).BuildCards(reader.ReadAll());

        foreach (var card in cards)
        {
            // SKU-only enforcement (DO) and plain UX state values must not render a
            // bogus "known to revert" badge
            if (card.Model.SettingId == "delivery-optimization" || card.Model.GroupId == "Update Experience")
                Assert.Null(card.Model.Enforcement);
            else
                Assert.Equal(EnforcementLevel.Enforced, card.Model.Enforcement!.Level);
        }
    }

    [Fact]
    public void PolicyCards_RequirePro_UxCards_HaveNoSkuRestriction()
    {
        var registry = new FakeRegistryService();
        registry.SetString(WindowsUpdateRegistryPaths.CurrentVersionKeyPath, "DisplayVersion", "24H2");
        var reader = new WindowsUpdateSettingsReader(registry);
        var cards = new WindowsUpdateCardProvider(reader).BuildCards(reader.ReadAll());

        foreach (var card in cards)
        {
            if (card.Model.GroupId == "Update Experience")
                Assert.Null(card.Model.SkuRestriction);
            else
                Assert.Equal(Core.Modules.WindowsSku.Pro, card.Model.SkuRestriction);
        }
    }

    [Fact]
    public void ToggleGroup_ReadsLiveState_AtStageTime()
    {
        var registry = new FakeRegistryService();
        var reader = new WindowsUpdateSettingsReader(registry);
        var cards = new WindowsUpdateCardProvider(reader).BuildCards(reader.ReadAll());
        var noAutoReboot = cards.Single(c => c.Model.SettingId == "no-auto-reboot");

        // Registry changes AFTER scan; staging must capture the live before-value.
        registry.SetDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoRebootWithLoggedOnUsers", 0);

        var group = noAutoReboot.CreateToggleGroup(true);

        Assert.Equal("0", group.Changes.Single().BeforeValue);
    }
}
