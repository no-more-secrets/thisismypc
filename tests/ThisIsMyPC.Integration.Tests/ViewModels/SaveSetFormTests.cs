using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Integration.Tests.Fakes;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

/// <summary>Story 8.5: Save as Set (review panel) and Create Set from Selection (history).</summary>
public sealed class SaveSetFormTests : IDisposable
{
    private readonly string _userDir = Path.Combine(Path.GetTempPath(), $"tipc-savesets-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_userDir, recursive: true); } catch (IOException) { }
    }

    private CustomSetWriter Writer => new(_userDir);

    private static ChangeDescriptor Descriptor(string settingId = "advertising-id") => new()
    {
        ModuleId = "Windows Annoyances",
        SettingId = settingId,
        DisplayName = "Advertising ID",
        SystemLocation = @"HKCU\Test",
        BeforeValue = "1",
        AfterValue = "0",
        BeforeDisplay = "Enabled",
        AfterDisplay = "Disabled",
        ValueType = ChangeValueType.Registry_DWord,
    };

    private static ChangeHistoryEntry HistoryEntry(long id, string? groupId, string settingId) => new()
    {
        Id = id,
        ModuleId = "Windows Annoyances",
        SettingId = settingId,
        DisplayName = settingId,
        SystemLocation = @"HKCU\Test",
        BeforeValue = "1",
        AfterValue = "0",
        BeforeDisplay = "Enabled",
        AfterDisplay = "Disabled",
        ValueType = ChangeValueType.Registry_DWord,
        GroupId = groupId,
        AppliedAt = DateTimeOffset.Now,
    };

    private ChangeHistoryViewModel CreateHistoryVm(IReadOnlyList<ChangeHistoryEntry> entries)
        => new(
            new FakeChangeHistoryServiceWithEntries(entries),
            _ => Task.FromResult(OperationResult<bool>.Success(true)),
            _ => Task.FromResult(OperationResult<bool>.Success(true)),
            Writer);

    // --- Review panel: Save as Set ---

    [Fact]
    public void ReviewPanel_SaveAsSet_WritesPendingChangesAsUserSet()
    {
        var pending = new PendingChangesService();
        pending.Stage(Descriptor());
        pending.Stage(Descriptor(settingId: "taskbar-widgets"));
        var vm = new ReviewPanelViewModel(pending, Writer);

        vm.SaveSetForm.OpenCommand.Execute(null);
        Assert.True(vm.SaveSetForm.IsOpen);
        vm.SaveSetForm.SetName = "My Baseline";
        vm.SaveSetForm.SetDescription = "Things I always turn off.";
        vm.SaveSetForm.SaveCommand.Execute(null);

        Assert.False(vm.SaveSetForm.IsOpen);
        Assert.Null(vm.SaveSetForm.ErrorMessage);
        Assert.Contains("my-baseline.json", vm.SaveSetForm.SuccessMessage, StringComparison.Ordinal);
        Assert.Contains("2 changes", vm.SaveSetForm.SuccessMessage, StringComparison.Ordinal);

        var loaded = new SetProvider(Path.Combine(_userDir, "no-builtin"), _userDir).LoadSets();
        var set = Assert.Single(loaded.Sets.Where(s => s.Name == "My Baseline"));
        Assert.Equal(SetSource.User, set.Source);
        Assert.Equal(SetCategory.TweakSet, set.Category);
        Assert.Equal(2, set.Entries.Count);
    }

    [Fact]
    public void ReviewPanel_SaveAsSet_OptimizationPackCategoryHonored()
    {
        var pending = new PendingChangesService();
        pending.Stage(Descriptor());
        var vm = new ReviewPanelViewModel(pending, Writer);

        vm.SaveSetForm.OpenCommand.Execute(null);
        vm.SaveSetForm.SetName = "Big Pack";
        vm.SaveSetForm.SetDescription = "Everything.";
        vm.SaveSetForm.IsOptimizationPack = true;
        vm.SaveSetForm.SaveCommand.Execute(null);

        var set = Assert.Single(
            new SetProvider(Path.Combine(_userDir, "nb"), _userDir).LoadSets().Sets);
        Assert.Equal(SetCategory.OptimizationPack, set.Category);
    }

    [Fact]
    public void ReviewPanel_SaveAsSet_MissingName_ShowsErrorAndStaysOpen()
    {
        var pending = new PendingChangesService();
        pending.Stage(Descriptor());
        var vm = new ReviewPanelViewModel(pending, Writer);

        vm.SaveSetForm.OpenCommand.Execute(null);
        vm.SaveSetForm.SetDescription = "no name given";
        vm.SaveSetForm.SaveCommand.Execute(null);

        Assert.True(vm.SaveSetForm.IsOpen);
        Assert.Equal("Set name is required.", vm.SaveSetForm.ErrorMessage);
        Assert.False(Directory.Exists(_userDir) && Directory.GetFiles(_userDir).Length > 0);
    }

    [Fact]
    public void ReviewPanel_SaveAsSet_NoPendingChanges_ReportsError()
    {
        var vm = new ReviewPanelViewModel(new PendingChangesService(), Writer);

        vm.SaveSetForm.OpenCommand.Execute(null);
        vm.SaveSetForm.SetName = "Empty";
        vm.SaveSetForm.SetDescription = "Nothing staged.";
        vm.SaveSetForm.SaveCommand.Execute(null);

        Assert.Equal("No changes to save as a set.", vm.SaveSetForm.ErrorMessage);
    }

    // --- History panel: Create Set from Selection ---

    [Fact]
    public async Task History_SelectedBatches_CreateSet_WritesOneEntryPerBatch()
    {
        // Batch "g1" has two rows (one toggle across two registry values); row 3 alone.
        var vm = CreateHistoryVm(
        [
            HistoryEntry(1, "g1", "copilot"),
            HistoryEntry(2, "g1", "copilot"),
            HistoryEntry(3, null, "taskbar-widgets"),
        ]);
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        var batches = vm.HistoryGroups.SelectMany(g => g.Batches).ToList();
        Assert.Equal(2, batches.Count);
        Assert.False(vm.HasSelection);

        foreach (var batch in batches)
            batch.IsSelected = true;
        Assert.True(vm.HasSelection);
        Assert.Equal(2, vm.SelectedBatchCount);

        vm.SaveSetForm.OpenCommand.Execute(null);
        vm.SaveSetForm.SetName = "From History";
        vm.SaveSetForm.SetDescription = "Replay of past changes.";
        vm.SaveSetForm.SaveCommand.Execute(null);

        Assert.Null(vm.SaveSetForm.ErrorMessage);
        Assert.Contains("2 changes", vm.SaveSetForm.SuccessMessage, StringComparison.Ordinal);

        var set = Assert.Single(
            new SetProvider(Path.Combine(_userDir, "nb"), _userDir).LoadSets().Sets);
        Assert.Equal(2, set.Entries.Count); // g1 collapsed to one entry + solo row
        Assert.Contains(set.Entries, e => e.SettingId == "copilot");
        Assert.Contains(set.Entries, e => e.SettingId == "taskbar-widgets");

        // Selection clears after a successful save.
        Assert.False(vm.HasSelection);
        Assert.All(batches, b => Assert.False(b.IsSelected));
    }

    [Fact]
    public async Task History_NoSelection_SaveReportsError()
    {
        var vm = CreateHistoryVm([HistoryEntry(1, null, "copilot")]);
        await vm.LoadHistoryCommand.ExecuteAsync(null);

        vm.SaveSetForm.OpenCommand.Execute(null);
        vm.SaveSetForm.SetName = "Nothing";
        vm.SaveSetForm.SetDescription = "No selection.";
        vm.SaveSetForm.SaveCommand.Execute(null);

        Assert.Equal("No changes to save as a set.", vm.SaveSetForm.ErrorMessage);
    }

    [Fact]
    public async Task History_Reload_ResetsSelection()
    {
        var vm = CreateHistoryVm([HistoryEntry(1, null, "copilot")]);
        await vm.LoadHistoryCommand.ExecuteAsync(null);
        vm.HistoryGroups.Single().Batches.Single().IsSelected = true;
        Assert.True(vm.HasSelection);

        await vm.LoadHistoryCommand.ExecuteAsync(null);

        Assert.False(vm.HasSelection);
    }

    // --- Set Loader labeling (AC 4) ---

    [Fact]
    public void WrittenCustomSet_AppearsAsUserCreatedInSetCard()
    {
        var pending = new PendingChangesService();
        pending.Stage(Descriptor());
        var vm = new ReviewPanelViewModel(pending, Writer);
        vm.SaveSetForm.OpenCommand.Execute(null);
        vm.SaveSetForm.SetName = "Mine";
        vm.SaveSetForm.SetDescription = "User made.";
        vm.SaveSetForm.SaveCommand.Execute(null);

        // The Set Loader re-reads disk on every open; a fresh provider load is what
        // MainWindowViewModel.OpenSetLoader performs.
        var set = Assert.Single(
            new SetProvider(Path.Combine(_userDir, "nb"), _userDir).LoadSets().Sets);
        var card = new SetItemViewModel(set);

        Assert.Contains("User", card.MetaLine, StringComparison.Ordinal);
        Assert.DoesNotContain("Built-in", card.MetaLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavedSet_AppearsUserLabeled_OnNextSetLoaderOpen_ThroughMainWindow()
    {
        // Full wiring: save from the review panel, then open the Set Loader the way
        // the app does (MainWindowViewModel re-reads disk on every open).
        var pendingChangesService = new PendingChangesService();
        var reviewPanel = new ReviewPanelViewModel(pendingChangesService, Writer);
        var setProvider = new SetProvider(Path.Combine(_userDir, "no-builtin"), _userDir);
        var vm = new MainWindowViewModel(
            new NavigationService([new FakeModule(name: "FakeModule")]),
            pendingChangesService,
            new FakeChangeHistoryService(),
            new FakeRegistryService(),
            new FakeExplorerRestartService(),
            reviewPanel,
            setProvider,
            [],
            Writer);
        await vm.InitializeAsync();

        pendingChangesService.Stage(Descriptor());
        reviewPanel.SaveSetForm.OpenCommand.Execute(null);
        reviewPanel.SaveSetForm.SetName = "Fresh Set";
        reviewPanel.SaveSetForm.SetDescription = "Saved moments ago.";
        reviewPanel.SaveSetForm.SaveCommand.Execute(null);
        Assert.Null(reviewPanel.SaveSetForm.ErrorMessage);

        vm.OpenSetLoaderCommand.Execute(null);

        var loader = Assert.IsType<SetLoaderViewModel>(vm.CurrentContent);
        var card = Assert.Single(loader.TweakSets, s => s.Name == "Fresh Set");
        Assert.Contains("User", card.MetaLine, StringComparison.Ordinal);
        Assert.DoesNotContain("Built-in", card.MetaLine, StringComparison.Ordinal);
    }
}
