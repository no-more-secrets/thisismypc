using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Annoyances;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Annoyances.Tests.Fakes;

namespace ThisIsMyPC.Modules.Annoyances.Tests;

public sealed class AnnoyancesSetEntryInspectorTests
{
    private readonly FakeRegistryService _registry = new();

    private AnnoyancesSetEntryInspector Inspector => new(_registry);

    private static SetEntry Entry(string settingId, string value) => new()
    {
        ModuleId = "Windows Annoyances",
        SettingId = settingId,
        Value = value,
        Description = "d",
    };

    [Fact]
    public void UnknownSettingId_ReturnsNull()
    {
        Assert.Null(Inspector.Inspect(Entry("no-such-setting", "0")));
    }

    [Fact]
    public void Single_DefaultState_NotApplied_ForSuppressEntry()
    {
        var state = Inspector.Inspect(Entry("advertising-id", "0"));

        Assert.NotNull(state);
        Assert.Equal("Disable the Advertising ID", state!.SettingDisplayName);
        Assert.Equal("1", state.CurrentValue);
        Assert.Equal("Windows default", state.CurrentDisplay);
        Assert.False(state.IsApplied);
    }

    [Fact]
    public void Single_SuppressedState_Applied_ForSuppressEntry()
    {
        _registry.SetDWord(AnnoyancesRegistryPaths.AdvertisingInfoKeyPath, "Enabled", 0);

        var state = Inspector.Inspect(Entry("advertising-id", "0"));

        Assert.Equal("Suppressed", state!.CurrentDisplay);
        Assert.True(state.IsApplied);
    }

    [Fact]
    public void Single_RestoreDirectionEntry_AppliedOnDefaultState()
    {
        var state = Inspector.Inspect(Entry("advertising-id", "1"));

        Assert.True(state!.IsApplied);
    }

    [Fact]
    public void CopilotGroup_DefaultState_WindowsDefault_NotApplied()
    {
        var state = Inspector.Inspect(Entry("copilot", "1"));

        Assert.Equal("Windows Copilot", state!.SettingDisplayName);
        Assert.Equal("0", state.CurrentValue);
        Assert.Equal("Windows default", state.CurrentDisplay);
        Assert.False(state.IsApplied);
    }

    [Fact]
    public void CopilotGroup_BothScopesSet_Suppressed_Applied()
    {
        _registry.SetDWord(AnnoyancesRegistryPaths.CopilotMachinePoliciesKeyPath, "TurnOffWindowsCopilot", 1);
        _registry.SetDWord(AnnoyancesRegistryPaths.CopilotUserPoliciesKeyPath, "TurnOffWindowsCopilot", 1);

        var state = Inspector.Inspect(Entry("copilot", "1"));

        Assert.Equal("Suppressed", state!.CurrentDisplay);
        Assert.True(state.IsApplied);
    }

    [Fact]
    public void CopilotGroup_OneScopeOnly_PartiallySet_NotAppliedEitherDirection()
    {
        _registry.SetDWord(AnnoyancesRegistryPaths.CopilotMachinePoliciesKeyPath, "TurnOffWindowsCopilot", 1);

        var suppress = Inspector.Inspect(Entry("copilot", "1"));
        var restore = Inspector.Inspect(Entry("copilot", "0"));

        Assert.Equal("Partially set", suppress!.CurrentDisplay);
        Assert.False(suppress.IsApplied);
        Assert.False(restore!.IsApplied);
    }

    [Fact]
    public void RecallGroup_MixedPolarity_AppliedOnlyWhenAllThreeSuppress()
    {
        var windowsAi = AnnoyancesRegistryPaths.WindowsAiPoliciesKeyPath;
        _registry.SetDWord(windowsAi, "AllowRecallEnablement", 0);
        _registry.SetDWord(windowsAi, "DisableAIDataAnalysis", 1);

        var partial = Inspector.Inspect(Entry("recall", "0"));
        Assert.Equal("Partially set", partial!.CurrentDisplay);
        Assert.False(partial.IsApplied);

        _registry.SetDWord(windowsAi, "TurnOffSavingSnapshots", 1);
        var full = Inspector.Inspect(Entry("recall", "0"));
        Assert.Equal("Suppressed", full!.CurrentDisplay);
        Assert.True(full.IsApplied);
    }

    [Fact]
    public void BingSearch_PartialState_NotAppliedEitherDirection()
    {
        // Only the search box policy set, BingSearchEnabled still default
        _registry.SetDWord(AnnoyancesRegistryPaths.ExplorerPoliciesKeyPath, "DisableSearchBoxSuggestions", 1);

        var suppress = Inspector.Inspect(Entry("bing-search", "0"));
        var restore = Inspector.Inspect(Entry("bing-search", "1"));

        Assert.Equal("Partially set", suppress!.CurrentDisplay);
        Assert.False(suppress.IsApplied);
        Assert.False(restore!.IsApplied);
    }

    [Fact]
    public void Group_UnrecognizedValue_NeverReadsAsApplied()
    {
        // A user-authored set with a bogus value must not preview as "already applied"
        // on a default machine
        var state = Inspector.Inspect(Entry("copilot", "2"));

        Assert.False(state!.IsApplied);
    }

    [Fact]
    public void BingSearch_FullySuppressed_Applied()
    {
        _registry.SetDWord(AnnoyancesRegistryPaths.SearchKeyPath, "BingSearchEnabled", 0);
        _registry.SetDWord(AnnoyancesRegistryPaths.ExplorerPoliciesKeyPath, "DisableSearchBoxSuggestions", 1);

        var state = Inspector.Inspect(Entry("bing-search", "0"));

        Assert.Equal("Suppressed", state!.CurrentDisplay);
        Assert.True(state.IsApplied);
    }

    [Fact]
    public void CreateChangeGroup_CopilotSuppress_BuildsBothScopes_WithEnforcement()
    {
        var group = Inspector.CreateChangeGroup(Entry("copilot", "1"));

        Assert.NotNull(group);
        Assert.Equal(2, group!.Changes.Count);
        Assert.All(group.Changes, c =>
        {
            Assert.Equal("copilot", c.SettingId);
            Assert.Equal("1", c.AfterValue);
            Assert.Equal("0", c.BeforeValue); // live before-value from the fake registry
            Assert.NotNull(c.Enforcement);
        });
    }

    [Fact]
    public void CreateChangeGroup_CopilotRestore_NoEnforcement()
    {
        var group = Inspector.CreateChangeGroup(Entry("copilot", "0"));

        Assert.All(group!.Changes, c =>
        {
            Assert.Equal("0", c.AfterValue);
            Assert.Null(c.Enforcement);
        });
    }

    [Fact]
    public void CreateChangeGroup_BogusValues_ReturnNull()
    {
        Assert.Null(Inspector.CreateChangeGroup(Entry("copilot", "2")));
        Assert.Null(Inspector.CreateChangeGroup(Entry("advertising-id", "banana")));
        Assert.Null(Inspector.CreateChangeGroup(Entry("bing-search", "2")));
        Assert.Null(Inspector.CreateChangeGroup(Entry("no-such-setting", "0")));
    }

    [Fact]
    public void CreateChangeGroup_RecallSuppress_MixedPolarityValues()
    {
        var group = Inspector.CreateChangeGroup(Entry("recall", "0"));

        Assert.Equal(3, group!.Changes.Count);
        var byValue = group.Changes.ToDictionary(
            c => c.SystemLocation[(c.SystemLocation.LastIndexOf('\\') + 1)..], c => c.AfterValue);
        Assert.Equal("0", byValue["AllowRecallEnablement"]);
        Assert.Equal("1", byValue["DisableAIDataAnalysis"]);
        Assert.Equal("1", byValue["TurnOffSavingSnapshots"]);
    }

    [Fact]
    public void CreateChangeGroup_Single_WrapsOneDescriptor_NullEnforcement()
    {
        var group = Inspector.CreateChangeGroup(Entry("advertising-id", "0"));

        var change = Assert.Single(group!.Changes);
        Assert.Equal("advertising-id", change.SettingId);
        Assert.Equal("0", change.AfterValue);
        Assert.Null(change.Enforcement);
    }

    [Fact]
    public void CreateChangeGroup_EdgeShortcuts_CarriesDriftEnforcement()
    {
        var group = Inspector.CreateChangeGroup(Entry("edge-shortcuts", "0"));

        var change = Assert.Single(group!.Changes);
        Assert.NotNull(change.Enforcement);
        Assert.Contains("Windows Update", change.Enforcement!.ReversionVectors);
    }

    [Fact]
    public void CreateChangeGroup_BingSearchSuppress_TwoOppositePolarityChanges()
    {
        var group = Inspector.CreateChangeGroup(Entry("bing-search", "0"));

        Assert.Equal(2, group!.Changes.Count);
        Assert.Contains(group.Changes, c => c.AfterValue == "0"); // BingSearchEnabled
        Assert.Contains(group.Changes, c => c.AfterValue == "1"); // DisableSearchBoxSuggestions
    }
}
