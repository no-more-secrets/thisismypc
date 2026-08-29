using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Modules.Annoyances.Models;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Annoyances.Tests.Fakes;

namespace ThisIsMyPC.Modules.Annoyances.Tests;

public sealed class AnnoyancesCardProviderTests
{
    private readonly FakeRegistryService _registry = new();

    private async Task<IReadOnlyList<SettingCardSource>> BuildAsync()
    {
        var scan = await new AnnoyancesModule(_registry).ScanSystemStateAsync();
        var provider = new AnnoyancesCardProvider(new AnnoyancesSettingsReader(_registry));
        return provider.BuildCards((AnnoyancesScanData)scan.Value!);
    }

    [Fact]
    public async Task BuildCards_CoversFullToggleInventory()
    {
        var cards = await BuildAsync();

        // 17 preference singles + Bing, suggested-content, Copilot policy, Recall groups.
        Assert.Equal(21, cards.Count);
        Assert.All(cards, c => Assert.Equal(SettingControlType.Toggle, c.Model.ControlType));
        Assert.All(cards, c => Assert.Equal("Windows Annoyances", c.Model.ModuleId));
        Assert.Equal(cards.Count, cards.Select(c => c.Model.SettingId).Distinct().Count());
        Assert.Contains(cards, c => c.Model.SettingId == "bing-search");
        Assert.Contains(cards, c => c.Model.SettingId == "settings-suggested-content");
        Assert.Contains(cards, c => c.Model.SettingId == "copilot");
        Assert.Contains(cards, c => c.Model.SettingId == "recall");
    }

    [Fact]
    public async Task BuildCards_GroupsBySectionHeaders_InSectionOrder()
    {
        var cards = await BuildAsync();

        string[] expectedGroups =
        [
            "Nag Screens & Suggestions",
            "Bing Search & Edge",
            "Advertising & Tracking",
            "Gaming & Accessibility",
            "AI Features",
        ];
        Assert.Equal(expectedGroups, cards.Select(c => c.Model.GroupId).Distinct());
    }

    [Fact]
    public async Task BuildCards_AiFeaturesOrderingInterleavesGroupsAndSingles()
    {
        var cards = await BuildAsync();

        var ai = cards.Where(c => c.Model.GroupId == "AI Features").Select(c => c.Model.SettingId).ToList();
        Assert.Equal(["copilot", "copilot-button", "recall", "edge-sidebar"], ai);
    }

    [Fact]
    public async Task BuildCards_EnforcementProfiles_OnDriftFragileEntries()
    {
        var cards = await BuildAsync();

        var copilot = cards.Single(c => c.Model.SettingId == "copilot");
        Assert.NotNull(copilot.Model.Enforcement);
        Assert.Equal(EnforcementLevel.Simple, copilot.Model.Enforcement!.Level);
        Assert.Contains("Windows feature updates", copilot.Model.Enforcement.ReversionRisks!);

        var bing = cards.Single(c => c.Model.SettingId == "bing-search");
        Assert.NotNull(bing.Model.Enforcement);
        Assert.Contains("Windows Update", bing.Model.Enforcement!.ReversionRisks!);

        // Plain toggles carry no enforcement profile.
        var scoobe = cards.Single(c => c.Model.SettingId == "scoobe-nags");
        Assert.Null(scoobe.Model.Enforcement);
    }

    [Fact]
    public async Task CreateToggleGroup_ProducesSameChangesAsTheFactories()
    {
        var cards = await BuildAsync();

        // Group toggle: Copilot policy stages both scopes atomically with shared id.
        var copilotGroup = cards.Single(c => c.Model.SettingId == "copilot").CreateToggleGroup(true);
        Assert.Equal(2, copilotGroup.Changes.Count);
        Assert.All(copilotGroup.Changes, ch => Assert.Equal("copilot", ch.SettingId));

        // Single toggle: wrapped in a one-descriptor group with live before-value.
        var scoobeGroup = cards.Single(c => c.Model.SettingId == "scoobe-nags").CreateToggleGroup(true);
        var change = Assert.Single(scoobeGroup.Changes);
        Assert.Equal("scoobe-nags", change.SettingId);
        Assert.Equal("0", change.AfterValue);
    }

    [Fact]
    public async Task ReadCurrentState_ReflectsLiveRegistry()
    {
        var cards = await BuildAsync();
        var scoobe = cards.Single(c => c.Model.SettingId == "scoobe-nags");

        Assert.False(scoobe.ReadCurrentState());

        // Suppress in the fake registry (ScoobeSystemSettingEnabled -> 0) and re-read.
        _registry.WriteDWord(AnnoyancesRegistryPaths.UserProfileEngagementKeyPath, "ScoobeSystemSettingEnabled", 0);
        Assert.True(scoobe.ReadCurrentState());
    }

    [Fact]
    public async Task BuildCards_RegistryDataPopulated()
    {
        var cards = await BuildAsync();

        Assert.All(cards, c =>
        {
            Assert.False(string.IsNullOrEmpty(c.Model.RegistryPath));
            Assert.False(string.IsNullOrEmpty(c.Model.ValueName));
            Assert.False(string.IsNullOrEmpty(c.Model.RegistryValueType));
            Assert.False(string.IsNullOrEmpty(c.Model.Description));
        });
    }
}
