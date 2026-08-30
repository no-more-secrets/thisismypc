using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Integration.Tests.Fakes;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Startup.Services;
using ThisIsMyPC.Modules.WindowsUpdate;
using ThisIsMyPC.Modules.WindowsUpdate.Services;

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
        Func<string, ModuleAvailability?>? availabilityLookup = null,
        Core.Services.IPendingChangesService? pendingChangesService = null)
    {
        var registry = new FakeRegistryService();
        var load = LoadBundledSets();

        // Seed the Startup fakes with every service/task the bundled sets reference, so
        // instance-scoped entries resolve (Manual/enabled = the not-yet-applied state).
        var services = new FakeServiceControlService();
        var tasks = new FakeScheduledTaskService();
        foreach (var entry in load.Sets.SelectMany(s => s.Entries).Where(e => e.ModuleId == "Startup & Services"))
        {
            if (entry.SettingId.StartsWith("service-starttype:", StringComparison.Ordinal))
                services.AddService(entry.SettingId["service-starttype:".Length..], ServiceState.Running, ServiceStartType.Manual);
            else if (entry.SettingId.StartsWith("scheduled-task:", StringComparison.Ordinal))
                tasks.AddTask(entry.SettingId["scheduled-task:".Length..]);
        }

        // The version-pin entry needs a readable DisplayVersion (the WU inspector skips
        // the pin on machines where it can't be read).
        var wuRegistry = new StoringFakeRegistryService();
        wuRegistry.WriteString(WindowsUpdateRegistryPaths.CurrentVersionKeyPath, "DisplayVersion", "24H2");

        return new SetLoaderViewModel(
            load,
            [
                new ShellSetEntryInspector(registry),
                new AnnoyancesSetEntryInspector(registry),
                new ThisIsMyPC.Modules.Privacy.Services.PrivacySetEntryInspector(registry),
                new StartupSetEntryInspector(services, tasks, registry, new FakeStartupFolderService()),
                new WindowsUpdateSetEntryInspector(wuRegistry),
            ],
            availabilityLookup ?? (_ => Available),
            pendingChangesService ?? new PendingChangesService());
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
            ["Clean Boot", "NukeCopilot", "Privacy Baseline", "Windows Update Control"],
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
            _ => null,
            new PendingChangesService());

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
            _ => new ModuleAvailability(IsAvailable: false, Reason: "Explorer is not running"),
            new PendingChangesService());

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
            _ => Available,
            new PendingChangesService());

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
    public void StageIncluded_StagesEachIncludedEntry_AndRowsFlipToAlreadyStaged()
    {
        var pending = new PendingChangesService();
        var vm = CreateWithBundledSets(pendingChangesService: pending);
        vm.SelectSetCommand.Execute(vm.TweakSets.Single(s => s.Name == "NukeCopilot"));
        Assert.Equal(3, vm.IncludedCount); // all three default-state entries checked

        vm.StageIncludedCommand.Execute(null);

        Assert.Equal(3, pending.PendingGroups.Count);
        Assert.Contains("3 changes staged", vm.StageMessage, StringComparison.Ordinal);
        // Preview re-resolved: every staged row now reads as already staged, unchecked
        var rows = vm.PreviewGroups.SelectMany(g => g.Entries).ToList();
        Assert.All(rows, r => Assert.True(r.IsAlreadyStaged));
        Assert.All(rows, r => Assert.False(r.IsIncluded));
        Assert.Equal(0, vm.IncludedCount);

        // The copilot group staged BOTH policy scopes atomically
        var copilotGroup = pending.PendingGroups.Single(
            g => g.Changes.Any(c => c.SettingId == "copilot"));
        Assert.Equal(2, copilotGroup.Changes.Count);
        Assert.All(copilotGroup.Changes, c => Assert.Equal("1", c.AfterValue));
    }

    [Fact]
    public void ConflictingPendingChange_IsExcludedByDefault_IncludingItReplacesThePendingGroup()
    {
        var pending = new PendingChangesService();
        // A pending change already restores the advertising ID (opposite direction)
        var registry = new FakeRegistryService();
        var conflicting = new Modules.Annoyances.Services.AnnoyancesSetEntryInspector(registry)
            .CreateChangeGroup(new SetEntry
            {
                ModuleId = "Windows Annoyances",
                SettingId = "advertising-id",
                Value = "1",
                Description = "d",
            });
        pending.Stage(conflicting!);
        var oldGroupId = conflicting!.GroupId;

        var vm = CreateWithBundledSets(pendingChangesService: pending);
        vm.SelectSetCommand.Execute(vm.TweakSets.Single(s => s.Name == "Privacy Baseline"));

        var row = vm.PreviewGroups.SelectMany(g => g.Entries)
            .Single(e => e.Entry.SettingId == "advertising-id");
        Assert.True(row.HasConflict);
        Assert.False(row.IsIncluded); // keep-pending is the default
        Assert.Contains("pending change", row.ConflictText, StringComparison.OrdinalIgnoreCase);

        // Stage only this row, resolved as replace
        foreach (var other in vm.PreviewGroups.SelectMany(g => g.Entries))
            other.IsIncluded = false;
        row.IsIncluded = true;
        vm.StageIncludedCommand.Execute(null);

        Assert.DoesNotContain(pending.PendingGroups, g => g.GroupId == oldGroupId);
        var replacement = pending.PendingGroups.Single(
            g => g.Changes.Any(c => c.SettingId == "advertising-id"));
        Assert.Equal("0", replacement.Changes.Single().AfterValue);
    }

    [Fact]
    public void ReIncludingAnAlreadyStagedRow_ReplacesInsteadOfDuplicating()
    {
        var pending = new PendingChangesService();
        var vm = CreateWithBundledSets(pendingChangesService: pending);
        vm.SelectSetCommand.Execute(vm.TweakSets.Single(s => s.Name == "NukeCopilot"));
        vm.StageIncludedCommand.Execute(null);
        Assert.Equal(3, pending.PendingGroups.Count);

        // Check every already-staged row and stage again: still 3 groups, no duplicates
        foreach (var row in vm.PreviewGroups.SelectMany(g => g.Entries))
            row.IsIncluded = true;
        vm.StageIncludedCommand.Execute(null);

        Assert.Equal(3, pending.PendingGroups.Count);
        Assert.Equal(1, pending.PendingGroups.Count(
            g => g.Changes.Any(c => c.SettingId == "copilot")));
    }

    [Fact]
    public void DuplicateEntriesInAUserSet_DoNotCrashThePreview()
    {
        var entry = new SetEntry
        {
            ModuleId = "Explorer",
            SettingId = "taskbar-widgets",
            Value = "0",
            Description = "d",
        };
        var registry = new FakeRegistryService();
        var vm = new SetLoaderViewModel(
            new SetLoadResult { Sets = [Definition(entry, entry)], Warnings = [] },
            [new ShellSetEntryInspector(registry)],
            _ => Available,
            new PendingChangesService());

        vm.SelectSetCommand.Execute(vm.TweakSets.Single());

        Assert.Equal(2, vm.PreviewGroups.SelectMany(g => g.Entries).Count());
    }

    [Fact]
    public void ExternalPendingChange_RefreshesThePreview()
    {
        var pending = new PendingChangesService();
        var vm = CreateWithBundledSets(pendingChangesService: pending);
        vm.SelectSetCommand.Execute(vm.TweakSets.Single(s => s.Name == "NukeCopilot"));
        Assert.Equal(3, vm.IncludedCount);

        // Something else (e.g. the module UI) stages a conflicting copilot change
        var registry = new FakeRegistryService();
        var external = new Modules.Annoyances.Services.AnnoyancesSetEntryInspector(registry)
            .CreateChangeGroup(new SetEntry
            {
                ModuleId = "Windows Annoyances",
                SettingId = "copilot",
                Value = "0",
                Description = "d",
            });
        pending.Stage(external!);

        var row = vm.PreviewGroups.SelectMany(g => g.Entries)
            .Single(e => e.Entry.SettingId == "copilot");
        Assert.True(row.HasConflict);
        Assert.Equal(2, vm.IncludedCount);
    }

    [Fact]
    public void AlreadyAppliedEntry_ExcludedByDefault_ButIncludable()
    {
        // taskbar-widgets scans as already hidden against the empty fake registry
        var vm = CreateWithBundledSets();
        vm.SelectSetCommand.Execute(vm.OptimizationPacks.Single());

        var row = vm.PreviewGroups.SelectMany(g => g.Entries)
            .Single(e => e.Entry.SettingId == "taskbar-widgets");
        Assert.True(row.IsApplied);
        Assert.False(row.IsIncluded);
        Assert.True(row.CanToggle);

        var before = vm.IncludedCount;
        row.IsIncluded = true;
        Assert.Equal(before + 1, vm.IncludedCount);
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
