using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Integration.Tests.Fakes;
using ThisIsMyPC.Modules.Annoyances;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.Integration.Tests.Sets;

/// <summary>
/// Story 8.4 AC 1-2 proof: a set staged from the Set Loader applies through the standard
/// pending-changes pipeline — enforcement-carrying entries route via the executor, the
/// registry writes land, and undo has faithful before-values. Pure fakes, no live system.
/// </summary>
public sealed class SetApplicationEndToEndTests
{
    [Fact]
    public async Task NukeCopilot_StagedFromSetLoader_AppliesThroughThePipeline()
    {
        var registry = new StoringFakeRegistryService();
        var module = new AnnoyancesModule(registry);
        var pending = new PendingChangesService(
            new EnforcementExecutor(new FakeServiceControlService()));

        var builtInDir = Path.Combine(AppContext.BaseDirectory, "sets");
        var loadResult = new SetProvider(
            builtInDir, Path.Combine(AppContext.BaseDirectory, "no-user-sets")).LoadSets();
        var vm = new SetLoaderViewModel(
            loadResult,
            [new AnnoyancesSetEntryInspector(registry), new ShellSetEntryInspector(registry)],
            _ => new ModuleAvailability(IsAvailable: true),
            pending);

        vm.SelectSetCommand.Execute(vm.TweakSets.Single(s => s.Name == "NukeCopilot"));
        vm.StageIncludedCommand.Execute(null);
        Assert.Equal(3, pending.PendingGroups.Count);

        var result = await pending.ApplyAllAsync(module.ApplyChangeAsync, module.RevertChangeAsync);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(4, result.Applied.Count); // copilot ×2 scopes + button + edge sidebar
        Assert.Empty(result.RolledBack);
        Assert.Equal(0, pending.PendingCount);

        // The writes landed exactly where TWEAKS.md says they should
        Assert.Equal(1, registry.ReadDWord(
            AnnoyancesRegistryPaths.CopilotMachinePoliciesKeyPath, "TurnOffWindowsCopilot").Value);
        Assert.Equal(1, registry.ReadDWord(
            AnnoyancesRegistryPaths.CopilotUserPoliciesKeyPath, "TurnOffWindowsCopilot").Value);
        Assert.Equal(0, registry.ReadDWord(
            AnnoyancesRegistryPaths.ExplorerAdvancedKeyPath, "ShowCopilotButton").Value);
        Assert.Equal(0, registry.ReadDWord(
            AnnoyancesRegistryPaths.EdgePoliciesKeyPath, "HubsSidebarEnabled").Value);

        // Before-values captured live for undo (missing values scanned as defaults)
        var copilotChange = result.Applied.First(c => c.SettingId == "copilot");
        Assert.Equal("0", copilotChange.BeforeValue);
        Assert.NotNull(copilotChange.Enforcement); // routed through the executor path
        // ...while the button proves the plain-delegate route (no enforcement); pinned
        // so this test keeps covering BOTH routes if factory metadata ever changes
        Assert.Null(result.Applied.Single(c => c.SettingId == "copilot-button").Enforcement);

        // Re-opening the preview now reads everything as already applied
        vm.SelectSetCommand.Execute(vm.TweakSets.Single(s => s.Name == "NukeCopilot"));
        Assert.All(
            vm.PreviewGroups.SelectMany(g => g.Entries),
            row => Assert.True(row.IsApplied));
    }
}
