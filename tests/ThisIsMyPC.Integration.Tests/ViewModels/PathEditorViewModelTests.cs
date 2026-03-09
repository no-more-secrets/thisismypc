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
    public void MoveUp_swaps_entries()
    {
        var vm = new PathEditorViewModel("A;B;C", "User", _pendingService);
        vm.MoveUpCommand.Execute(vm.Entries[1]);

        Assert.Equal("B", vm.Entries[0].Path);
        Assert.Equal("A", vm.Entries[1].Path);
        Assert.Equal("C", vm.Entries[2].Path);
    }

    [Fact]
    public void MoveDown_swaps_entries()
    {
        var vm = new PathEditorViewModel("A;B;C", "User", _pendingService);
        vm.MoveDownCommand.Execute(vm.Entries[0]);

        Assert.Equal("B", vm.Entries[0].Path);
        Assert.Equal("A", vm.Entries[1].Path);
        Assert.Equal("C", vm.Entries[2].Path);
    }

    [Fact]
    public void MoveUp_first_entry_no_op()
    {
        var vm = new PathEditorViewModel("A;B;C", "User", _pendingService);
        vm.MoveUpCommand.Execute(vm.Entries[0]);

        Assert.Equal("A", vm.Entries[0].Path);
        Assert.Equal("B", vm.Entries[1].Path);
    }

    [Fact]
    public void MoveDown_last_entry_no_op()
    {
        var vm = new PathEditorViewModel("A;B;C", "User", _pendingService);
        vm.MoveDownCommand.Execute(vm.Entries[2]);

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
    public void CharacterCountText_shows_total_length()
    {
        var vm = new PathEditorViewModel("AB;CD;EF", "User", _pendingService);
        // 6 chars (AB + CD + EF) + 2 semicolons = 8 characters
        Assert.Contains("8 characters", vm.CharacterCountText);
    }
}
