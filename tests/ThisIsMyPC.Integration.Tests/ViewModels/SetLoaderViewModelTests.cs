using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Integration.Tests.Fakes;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class SetLoaderViewModelTests
{
    private static readonly ModuleAvailability Available = new(IsAvailable: true);

    private static SetLoadResult LoadBundledSets()
    {
        var builtInDir = Path.Combine(AppContext.BaseDirectory, "sets");
        var userDir = Path.Combine(AppContext.BaseDirectory, "no-such-user-sets");
        return new SetProvider(builtInDir, userDir).LoadSets();
    }

    private static SetLoaderViewModel CreateWithBundledSets(
        Func<string, ModuleAvailability?>? availabilityLookup = null)
    {
        var registry = new FakeRegistryService();
        return new SetLoaderViewModel(
            LoadBundledSets(),
            [new ShellSetEntryInspector(registry), new AnnoyancesSetEntryInspector(registry)],
            availabilityLookup ?? (_ => Available));
    }

    private static SetDefinition Definition(params SetEntry[] entries) => new()
    {
        Name = "Test Set",
        Description = "d",
        Category = SetCategory.TweakSet,
        Version = "1.0.0",
        Author = "test",
        Entries = entries,
        Source = SetSource.User,
        FilePath = "test.json",
    };

    [Fact]
    public void BundledSets_AreCategorized()
    {
        var vm = CreateWithBundledSets();

        Assert.Equal(
            ["NukeCopilot", "Privacy Baseline"],
            vm.TweakSets.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(["Windows 10-ify"], vm.OptimizationPacks.Select(s => s.Name));
        Assert.False(vm.HasNoSets);
        Assert.True(vm.HasNoSelection);
    }

    [Fact]
    public void SetCard_ShowsCountAndModulesAffected()
    {
        var vm = CreateWithBundledSets();
        var pack = vm.OptimizationPacks.Single();

        Assert.Contains("changes", pack.MetaLine, StringComparison.Ordinal);
        Assert.Contains("Explorer", pack.ModulesAffected, StringComparison.Ordinal);
        Assert.Contains("Windows Annoyances", pack.ModulesAffected, StringComparison.Ordinal);
        Assert.Contains("Built-in", pack.MetaLine, StringComparison.Ordinal);
    }

    [Fact]
    public void OptimizationPackPreview_GroupsByConstituentSet_NothingSkipped()
    {
        var vm = CreateWithBundledSets();

        vm.SelectSetCommand.Execute(vm.OptimizationPacks.Single());

        Assert.Equal(
            ["Classic shell", "Taskbar", "Tips & suggestions", "NukeCopilot"],
            vm.PreviewGroups.Select(g => g.GroupName));
        Assert.All(vm.PreviewGroups, g => Assert.True(g.HasHeader));
        var entries = vm.PreviewGroups.SelectMany(g => g.Entries).ToList();
        Assert.All(entries, e => Assert.False(e.IsSkipped));
        // Fake registry = Windows defaults, so nothing reads as applied — except
        // taskbar-widgets, whose reader treats the missing value as already hidden.
        Assert.All(
            entries.Where(e => e.Entry.SettingId != "taskbar-widgets"),
            e => Assert.False(e.IsApplied));
        Assert.True(entries.Single(e => e.Entry.SettingId == "taskbar-widgets").IsApplied);
    }

    [Fact]
    public void TweakSetPreview_IsASingleHeaderlessGroup_WithResolvedValues()
    {
        var vm = CreateWithBundledSets();
        var nukeCopilot = vm.TweakSets.Single(s => s.Name == "NukeCopilot");

        vm.SelectSetCommand.Execute(nukeCopilot);

        var group = Assert.Single(vm.PreviewGroups);
        Assert.False(group.HasHeader);

        var copilot = group.Entries.Single(e => e.Entry.SettingId == "copilot");
        Assert.Equal("Windows Copilot", copilot.SettingName);
        Assert.Equal("Windows default", copilot.CurrentDisplay);
        Assert.Equal("Disabled", copilot.ProposedDisplay);
        Assert.True(nukeCopilot.IsSelected);
    }

    [Fact]
    public void UnknownModule_IsMarkedSkipped_WithModuleName()
    {
        var vm = new SetLoaderViewModel(
            new SetLoadResult { Sets = [Definition(new SetEntry
            {
                ModuleId = "No Such Module",
                SettingId = "x",
                Value = "0",
                Description = "d",
            })], Warnings = [] },
            [],
            _ => null);

        vm.SelectSetCommand.Execute(vm.TweakSets.Single());

        var entry = Assert.Single(Assert.Single(vm.PreviewGroups).Entries);
        Assert.True(entry.IsSkipped);
        Assert.Contains("No Such Module", entry.SkipReason, StringComparison.Ordinal);
        Assert.Contains("skipped", entry.SkipReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnavailableModule_IsMarkedSkipped_WithReason()
    {
        var vm = new SetLoaderViewModel(
            new SetLoadResult { Sets = [Definition(new SetEntry
            {
                ModuleId = "Explorer",
                SettingId = "taskbar-widgets",
                Value = "0",
                Description = "d",
            })], Warnings = [] },
            [],
            _ => new ModuleAvailability(IsAvailable: false, Reason: "Explorer is not running"));

        vm.SelectSetCommand.Execute(vm.TweakSets.Single());

        var entry = Assert.Single(Assert.Single(vm.PreviewGroups).Entries);
        Assert.True(entry.IsSkipped);
        Assert.Contains("Explorer is not running", entry.SkipReason, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownSettingOnKnownModule_IsMarkedSkipped()
    {
        var registry = new FakeRegistryService();
        var vm = new SetLoaderViewModel(
            new SetLoadResult { Sets = [Definition(new SetEntry
            {
                ModuleId = "Explorer",
                SettingId = "renamed-away",
                Value = "0",
                Description = "d",
            })], Warnings = [] },
            [new ShellSetEntryInspector(registry)],
            _ => Available);

        vm.SelectSetCommand.Execute(vm.TweakSets.Single());

        var entry = Assert.Single(Assert.Single(vm.PreviewGroups).Entries);
        Assert.True(entry.IsSkipped);
        Assert.Contains("not recognized", entry.SkipReason, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryBundledEntry_ResolvesThroughAnInspector()
    {
        // The 8-6 authoring guarantee, exercised end-to-end through the preview path:
        // no bundled entry may fall into any skip bucket when all modules are available.
        var vm = CreateWithBundledSets();

        foreach (var set in vm.TweakSets.Concat(vm.OptimizationPacks))
        {
            vm.SelectSetCommand.Execute(set);
            Assert.All(
                vm.PreviewGroups.SelectMany(g => g.Entries),
                e => Assert.False(e.IsSkipped, $"{set.Name}/{e.Entry.SettingId}: {e.SkipReason}"));
        }
    }

    [Fact]
    public void SelectingASecondSet_ReplacesPreviewAndSelection()
    {
        var vm = CreateWithBundledSets();
        var first = vm.TweakSets[0];
        var second = vm.TweakSets[1];

        vm.SelectSetCommand.Execute(first);
        vm.SelectSetCommand.Execute(second);

        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.Same(second, vm.SelectedSet);
        Assert.Equal(
            second.Definition.Entries.Count,
            vm.PreviewGroups.SelectMany(g => g.Entries).Count());
    }
}
