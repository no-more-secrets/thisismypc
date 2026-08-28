using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Annoyances;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Models;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Annoyances.Tests.Fakes;

namespace ThisIsMyPC.Modules.Annoyances.Tests;

public sealed class AiFeaturesTests
{
    private readonly FakeRegistryService _registry = new();

    private AnnoyancesSettingsReader Reader => new(_registry);

    [Fact]
    public void Section_ContainsButtonAndEdgeSidebarSingles()
    {
        var prefs = Reader.ReadAll().Where(p => p.Section == AnnoyanceSection.AiFeatures).ToList();

        Assert.Equal(["copilot-button", "edge-sidebar"], prefs.Select(p => p.Id));
    }

    [Fact]
    public void CopilotButton_TargetsExplorerAdvanced_WithExplorerRestart()
    {
        var pref = Reader.ReadAll().Single(p => p.Id == "copilot-button");

        Assert.Equal(AnnoyancesRegistryPaths.ExplorerAdvancedKeyPath, pref.RegistryKeyPath);
        Assert.Equal("ShowCopilotButton", pref.RegistryValueName);
        Assert.Equal(RestartRequirement.ExplorerRestart, pref.RestartRequirement);
        Assert.Equal("0", pref.SuppressedValue);
        Assert.Equal("1", pref.DefaultValue);

        // Plain toggle: no enforcement on either direction
        Assert.Null(AnnoyanceChangeFactory.CreateToggle(pref, suppress: true).Enforcement);
    }

    [Fact]
    public void EdgeSidebar_TargetsEdgePolicy_NoRestart()
    {
        var pref = Reader.ReadAll().Single(p => p.Id == "edge-sidebar");

        Assert.Equal(AnnoyancesRegistryPaths.EdgePoliciesKeyPath, pref.RegistryKeyPath);
        Assert.Equal("HubsSidebarEnabled", pref.RegistryValueName);
        Assert.Equal(RestartRequirement.None, pref.RestartRequirement);
        Assert.Equal("0", pref.SuppressedValue);
        Assert.Equal("1", pref.DefaultValue);
    }

    [Fact]
    public void CopilotPolicy_CoversMachineAndUserScope_InvertedPolarity()
    {
        var prefs = Reader.ReadCopilotPolicy();

        Assert.Equal(2, prefs.Count);
        Assert.All(prefs, p =>
        {
            Assert.Equal("copilot", p.Id);
            Assert.Equal("TurnOffWindowsCopilot", p.RegistryValueName);
            Assert.Equal("1", p.SuppressedValue);
            Assert.Equal("0", p.DefaultValue);
            Assert.Equal("0", p.CurrentValue); // missing policy = Copilot on
            Assert.False(p.IsSuppressed);
            Assert.Equal(RestartRequirement.ExplorerRestart, p.RestartRequirement);
        });
        Assert.Equal(
            [AnnoyancesRegistryPaths.CopilotMachinePoliciesKeyPath,
             AnnoyancesRegistryPaths.CopilotUserPoliciesKeyPath],
            prefs.Select(p => p.RegistryKeyPath));
    }

    [Fact]
    public void CopilotPolicyToggle_SuppressWritesOneInBothScopes_WithDriftEnforcement()
    {
        var group = AnnoyanceChangeFactory.CreateCopilotPolicyToggle(Reader.ReadCopilotPolicy(), suppress: true);

        Assert.Equal(2, group.Changes.Count);
        Assert.All(group.Changes, c =>
        {
            Assert.Equal("copilot", c.SettingId);
            Assert.Equal("0", c.BeforeValue);
            Assert.Equal("1", c.AfterValue);
            Assert.NotNull(c.Enforcement);
            Assert.Contains("Windows feature updates", c.Enforcement!.ReversionVectors);
        });
    }

    [Fact]
    public void CopilotPolicyToggle_RestoreWritesZero_NullEnforcement()
    {
        _registry.SetDWord(AnnoyancesRegistryPaths.CopilotMachinePoliciesKeyPath, "TurnOffWindowsCopilot", 1);
        _registry.SetDWord(AnnoyancesRegistryPaths.CopilotUserPoliciesKeyPath, "TurnOffWindowsCopilot", 1);

        var prefs = Reader.ReadCopilotPolicy();
        Assert.All(prefs, p => Assert.True(p.IsSuppressed));

        var group = AnnoyanceChangeFactory.CreateCopilotPolicyToggle(prefs, suppress: false);
        Assert.All(group.Changes, c =>
        {
            Assert.Equal("1", c.BeforeValue);
            Assert.Equal("0", c.AfterValue);
            Assert.Null(c.Enforcement);
        });
    }

    [Fact]
    public void Recall_MixedPolarity_SuppressesPerValue()
    {
        var prefs = Reader.ReadRecall();

        Assert.Equal(3, prefs.Count);
        Assert.All(prefs, p =>
        {
            Assert.Equal("recall", p.Id);
            Assert.Equal(AnnoyancesRegistryPaths.WindowsAiPoliciesKeyPath, p.RegistryKeyPath);
            Assert.False(p.IsSuppressed); // all missing = Windows default
        });

        var group = AnnoyanceChangeFactory.CreateGroupToggle(
            prefs, "recall", "Recall", "d", suppress: true);
        var byValue = group.Changes.ToDictionary(
            c => c.SystemLocation[(c.SystemLocation.LastIndexOf('\\') + 1)..], c => c);

        Assert.Equal("0", byValue["AllowRecallEnablement"].AfterValue);
        Assert.Equal("1", byValue["DisableAIDataAnalysis"].AfterValue);
        Assert.Equal("1", byValue["TurnOffSavingSnapshots"].AfterValue);
        Assert.All(group.Changes, c => Assert.Null(c.Enforcement));

        var restore = AnnoyanceChangeFactory.CreateGroupToggle(
            prefs, "recall", "Recall", "d", suppress: false);
        var restoreByValue = restore.Changes.ToDictionary(
            c => c.SystemLocation[(c.SystemLocation.LastIndexOf('\\') + 1)..], c => c);
        Assert.Equal("1", restoreByValue["AllowRecallEnablement"].AfterValue);
        Assert.Equal("0", restoreByValue["DisableAIDataAnalysis"].AfterValue);
        Assert.Equal("0", restoreByValue["TurnOffSavingSnapshots"].AfterValue);
    }

    [Fact]
    public void Recall_PartiallySet_ScansMixedSuppressionStates()
    {
        // Only AllowRecallEnablement blocked; the other two still default
        _registry.SetDWord(AnnoyancesRegistryPaths.WindowsAiPoliciesKeyPath, "AllowRecallEnablement", 0);

        var prefs = Reader.ReadRecall();

        Assert.True(prefs.Single(p => p.RegistryValueName == "AllowRecallEnablement").IsSuppressed);
        Assert.False(prefs.Single(p => p.RegistryValueName == "DisableAIDataAnalysis").IsSuppressed);
        Assert.False(prefs.Single(p => p.RegistryValueName == "TurnOffSavingSnapshots").IsSuppressed);
    }

    [Fact]
    public async Task CopilotPolicyGroup_AppliesThroughModule_ToBothScopes()
    {
        var module = new AnnoyancesModule(_registry);
        var group = AnnoyanceChangeFactory.CreateCopilotPolicyToggle(Reader.ReadCopilotPolicy(), suppress: true);

        foreach (var change in group.Changes)
        {
            var result = await module.ApplyChangeAsync(change);
            Assert.True(result.IsSuccess, result.ErrorMessage);
        }

        Assert.Equal(1, _registry.ReadDWord(
            AnnoyancesRegistryPaths.CopilotMachinePoliciesKeyPath, "TurnOffWindowsCopilot").Value);
        Assert.Equal(1, _registry.ReadDWord(
            AnnoyancesRegistryPaths.CopilotUserPoliciesKeyPath, "TurnOffWindowsCopilot").Value);
    }
}
