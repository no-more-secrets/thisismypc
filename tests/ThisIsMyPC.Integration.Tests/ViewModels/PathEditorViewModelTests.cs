using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class PathEditorViewModelTests
{
    private readonly IPendingChangesService _pendingService = new PendingChangesService();

    [Fact]
    public void Constructor_splits_path_by_semicolon()
    {
        var vm = new PathEditorViewModel("A;B;C", "User", _pendingService);
        Assert.Equal(3, vm.Entries.Count);
        Assert.Equal("A", vm.Entries[0].Path);
        Assert.Equal("B", vm.Entries[1].Path);
        Assert.Equal("C", vm.Entries[2].Path);
    }

    [Fact]
    public void Constructor_strips_empty_entries()
    {
        var vm = new PathEditorViewModel("A;;B;", "User", _pendingService);
        Assert.Equal(2, vm.Entries.Count);
        Assert.Equal("A", vm.Entries[0].Path);
        Assert.Equal("B", vm.Entries[1].Path);
    }

    [Fact]
    public void MoveEntry_swaps_entries()
    {
        var vm = new PathEditorViewModel("A;B;C", "User", _pendingService);
        vm.MoveEntry(1, 0);

        Assert.Equal("B", vm.Entries[0].Path);
        Assert.Equal("A", vm.Entries[1].Path);
        Assert.Equal("C", vm.Entries[2].Path);
    }

    [Fact]
    public void MoveEntry_down()
    {
        var vm = new PathEditorViewModel("A;B;C", "User", _pendingService);
        vm.MoveEntry(0, 1);

        Assert.Equal("B", vm.Entries[0].Path);
        Assert.Equal("A", vm.Entries[1].Path);
        Assert.Equal("C", vm.Entries[2].Path);
    }

    [Fact]
    public void MoveEntry_same_index_no_op()
    {
        var vm = new PathEditorViewModel("A;B;C", "User", _pendingService);
        vm.MoveEntry(0, 0);

        Assert.Equal("A", vm.Entries[0].Path);
        Assert.Equal("B", vm.Entries[1].Path);
    }

    [Fact]
    public void MoveEntry_out_of_range_no_op()
    {
        var vm = new PathEditorViewModel("A;B;C", "User", _pendingService);
        vm.MoveEntry(-1, 0);
        vm.MoveEntry(0, 5);

        Assert.Equal("A", vm.Entries[0].Path);
        Assert.Equal("B", vm.Entries[1].Path);
        Assert.Equal("C", vm.Entries[2].Path);
    }

    [Fact]
    public void AddEntry_appends_to_list()
    {
        var vm = new PathEditorViewModel("A;B", "User", _pendingService);
        vm.AddEntryCommand.Execute(null);

        Assert.Equal(3, vm.Entries.Count);
        Assert.Equal("", vm.Entries[2].Path);
    }

    [Fact]
    public void RemoveEntry_removes_from_list()
    {
        var vm = new PathEditorViewModel("A;B;C", "User", _pendingService);
        vm.RemoveEntryCommand.Execute(vm.Entries[1]);

        Assert.Equal(2, vm.Entries.Count);
        Assert.Equal("A", vm.Entries[0].Path);
        Assert.Equal("C", vm.Entries[1].Path);
    }

    [Fact]
    public void GenerateDiff_shows_added_entries()
    {
        var diff = PathEditorViewModel.GenerateDiff("A;B", "A;B;C");
        Assert.Contains("+Added: C", diff);
    }

    [Fact]
    public void GenerateDiff_shows_removed_entries()
    {
        var diff = PathEditorViewModel.GenerateDiff("A;B;C", "A;B");
        Assert.Contains("-Removed: C", diff);
    }

    [Fact]
    public void Entries_have_correct_indices()
    {
        var vm = new PathEditorViewModel("X;Y;Z", "User", _pendingService);
        Assert.Equal(1, vm.Entries[0].Index);
        Assert.Equal(2, vm.Entries[1].Index);
        Assert.Equal(3, vm.Entries[2].Index);
    }

    [Fact]
    public void CharacterCountText_shows_entry_count_and_total_length()
    {
        var vm = new PathEditorViewModel("AB;CD;EF", "User", _pendingService);
        // 3 entries, 6 chars (AB + CD + EF) + 2 semicolons = 8 characters
        Assert.Contains("3 entries", vm.CharacterCountText);
        Assert.Contains("8 characters", vm.CharacterCountText);
    }

    [Fact]
    public void MoveEntry_stages_single_change_not_duplicates()
    {
        var vm = new PathEditorViewModel("A;B;C", "User", _pendingService);
        vm.MoveEntry(0, 2);
        vm.MoveEntry(1, 0);

        // Two moves should result in only one pending change group, not two
        Assert.Equal(1, _pendingService.PendingCount);
    }

    [Fact]
    public void MoveEntry_back_to_original_unstages_change()
    {
        var vm = new PathEditorViewModel("A;B;C", "User", _pendingService);
        vm.MoveEntry(0, 2); // A;B;C → B;C;A
        Assert.Equal(1, _pendingService.PendingCount);

        vm.MoveEntry(2, 0); // B;C;A → A;B;C (back to original)
        Assert.Equal(0, _pendingService.PendingCount);
    }

    [Fact]
    public void DiscardAll_resets_entries_to_original()
    {
        var vm = new PathEditorViewModel("A;B;C", "User", _pendingService);
        vm.MoveEntry(0, 2); // A;B;C → B;C;A

        Assert.Equal("B", vm.Entries[0].Path);

        _pendingService.DiscardAll();

        // Entries should reset to original order
        Assert.Equal("A", vm.Entries[0].Path);
        Assert.Equal("B", vm.Entries[1].Path);
        Assert.Equal("C", vm.Entries[2].Path);
    }
}
