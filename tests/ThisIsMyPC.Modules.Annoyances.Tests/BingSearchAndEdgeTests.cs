using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Annoyances.Tests.Fakes;

namespace ThisIsMyPC.Modules.Annoyances.Tests;

public sealed class BingSearchAndEdgeTests
{
    private readonly FakeRegistryService _registry = new();

    private AnnoyancesSettingsReader Reader => new(_registry);

    [Fact]
    public void BingSearch_MissingValues_ScansAsNotSuppressed()
    {
        var state = Reader.ReadBingSearch();

        Assert.Equal("1", state.BingSearchEnabledValue);
        Assert.Equal("0", state.DisableSearchBoxSuggestionsValue);
        Assert.False(state.IsSuppressed);
    }

    [Fact]
    public void BingSearch_SuppressedOnlyWhenBothValuesSuppress()
    {
        _registry.SetDWord(AnnoyancesRegistryPaths.SearchKeyPath, "BingSearchEnabled", 0);
        Assert.False(Reader.ReadBingSearch().IsSuppressed); // suggestions policy still missing

        _registry.SetDWord(AnnoyancesRegistryPaths.ExplorerPoliciesKeyPath, "DisableSearchBoxSuggestions", 1);
        Assert.True(Reader.ReadBingSearch().IsSuppressed);
    }

    [Fact]
    public void BingSearchToggle_Suppress_TwoDescriptorsOppositePolarities()
    {
        var group = AnnoyanceChangeFactory.CreateBingSearchToggle(Reader.ReadBingSearch(), suppress: true);

        Assert.Equal(2, group.Changes.Count);
        Assert.All(group.Changes, c => Assert.Equal("bing-search", c.SettingId));
        Assert.All(group.Changes, c => Assert.Equal(RestartRequirement.ExplorerRestart, c.RestartRequirement));

        var bing = group.Changes.Single(c => c.SystemLocation.EndsWith("BingSearchEnabled", StringComparison.Ordinal));
        Assert.Equal("1", bing.BeforeValue);
        Assert.Equal("0", bing.AfterValue);

        var suggestions = group.Changes.Single(c =>
            c.SystemLocation.EndsWith("DisableSearchBoxSuggestions", StringComparison.Ordinal));
        Assert.Equal("0", suggestions.BeforeValue);
        Assert.Equal("1", suggestions.AfterValue);
    }

    [Fact]
    public void BingSearchToggle_CarriesInformationalReversionVectors_OnSuppressOnly()
    {
        var suppressGroup = AnnoyanceChangeFactory.CreateBingSearchToggle(Reader.ReadBingSearch(), suppress: true);
        Assert.All(suppressGroup.Changes, c =>
        {
            Assert.NotNull(c.Enforcement);
            Assert.Equal(
                ["Windows Update", "Web Experience Pack deployment"],
                c.Enforcement!.ReversionVectors);
            Assert.Null(c.Enforcement.CompanionServices);
            Assert.Null(c.Enforcement.CompanionTasks);
            Assert.Null(c.Enforcement.GPCacheEntries);
            Assert.False(c.Enforcement.OwnerModeRequired);
            Assert.False(c.Enforcement.AclElevation);
        });

        var restoreGroup = AnnoyanceChangeFactory.CreateBingSearchToggle(Reader.ReadBingSearch(), suppress: false);
        Assert.All(restoreGroup.Changes, c => Assert.Null(c.Enforcement));
    }

    [Fact]
    public void EdgeShortcuts_TargetsHklmEdgeUpdatePolicy()
    {
        var pref = Reader.ReadAll().Single(p => p.Id == "edge-shortcuts");

        Assert.Equal(@"HKLM\SOFTWARE\Policies\Microsoft\EdgeUpdate", pref.RegistryKeyPath);
        Assert.Equal("CreateDesktopShortcutDefault", pref.RegistryValueName);
        Assert.Equal("0", pref.SuppressedValue);
        Assert.False(pref.IsSuppressed); // missing value = Edge creates shortcuts

        var change = AnnoyanceChangeFactory.CreateDriftFragileToggle(pref, suppress: true);
        Assert.NotNull(change.Enforcement);
        Assert.Equal("0", change.AfterValue);

        var restore = AnnoyanceChangeFactory.CreateDriftFragileToggle(pref, suppress: false);
        Assert.Null(restore.Enforcement);
    }

    [Fact]
    public async Task BingSearchGroup_SecondWriteFails_FirstValueRolledBack()
    {
        // The atomicity guarantee: BingSearchEnabled succeeds, the Policies write fails,
        // rollback must RESTORE BingSearchEnabled — not re-apply the suppressing value.
        _registry.SetDWord(AnnoyancesRegistryPaths.SearchKeyPath, "BingSearchEnabled", 1);
        _registry.SetWriteFailure(
            AnnoyancesRegistryPaths.ExplorerPoliciesKeyPath, Core.Results.ErrorCategory.AccessDenied);

        var module = new AnnoyancesModule(_registry);
        var pendingChanges = new PendingChangesService(new PassthroughEnforcementExecutor());
        pendingChanges.Stage(AnnoyanceChangeFactory.CreateBingSearchToggle(Reader.ReadBingSearch(), suppress: true));

        var result = await pendingChanges.ApplyAllAsync(module.ApplyChangeAsync, module.RevertChangeAsync);

        Assert.False(result.IsSuccess);
        Assert.Single(result.RolledBack);
        Assert.Equal(1, _registry.ReadDWord(AnnoyancesRegistryPaths.SearchKeyPath, "BingSearchEnabled").Value);
        Assert.False(Reader.ReadBingSearch().IsSuppressed);
    }

    [Fact]
    public async Task BingSearchGroup_AppliesAtomicallyThroughPendingPipeline()
    {
        var module = new AnnoyancesModule(_registry);
        var pendingChanges = new PendingChangesService(new PassthroughEnforcementExecutor());
        pendingChanges.Stage(AnnoyanceChangeFactory.CreateBingSearchToggle(Reader.ReadBingSearch(), suppress: true));

        var result = await pendingChanges.ApplyAllAsync(module.ApplyChangeAsync, module.RevertChangeAsync);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(0, _registry.ReadDWord(AnnoyancesRegistryPaths.SearchKeyPath, "BingSearchEnabled").Value);
        Assert.Equal(1, _registry.ReadDWord(
            AnnoyancesRegistryPaths.ExplorerPoliciesKeyPath, "DisableSearchBoxSuggestions").Value);
        Assert.True(Reader.ReadBingSearch().IsSuppressed);
    }
}
