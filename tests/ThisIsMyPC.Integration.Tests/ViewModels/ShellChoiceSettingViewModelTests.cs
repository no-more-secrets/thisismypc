using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class ShellChoiceSettingViewModelTests
{
    private static ShellScanData MakeScanData() => new(
        ExplorerPreferences: [],
        // Fake registry fails every read, so the read-at-stage-time baselines
        // fall back to the Windows defaults (search box = 3, combining = 0),
        // matching these scan values.
        Taskbar: new TaskbarSettings(1, true, false, false, SearchboxMode: 3, ButtonCombining: 0));

    private static async Task WaitForStagingAsync(PendingChangesService pending, int expectedGroups)
    {
        for (var i = 0; i < 100 && pending.PendingGroups.Count != expectedGroups; i++)
            await Task.Delay(50);
        Assert.Equal(expectedGroups, pending.PendingGroups.Count);
    }

    [Fact]
    public void Shell_view_model_exposes_both_taskbar_choice_settings()
    {
        var pending = new PendingChangesService();
        var vm = new ShellViewModel(MakeScanData(), pending, new Fakes.FakeRegistryService());

        Assert.Equal(2, vm.TaskbarChoiceSettings.Count);
        Assert.Equal("Taskbar search", vm.TaskbarChoiceSettings[0].Label);
        Assert.Equal(4, vm.TaskbarChoiceSettings[0].Options.Count);
        Assert.Equal("Combine taskbar buttons", vm.TaskbarChoiceSettings[1].Label);
        Assert.Equal(3, vm.TaskbarChoiceSettings[1].Options.Count);
        Assert.Equal("Search box", vm.TaskbarChoiceSettings[0].SelectedOption?.DisplayName);
    }

    [Fact]
    public async Task Selecting_a_different_value_stages_a_modify_group()
    {
        var pending = new PendingChangesService();
        var vm = new ShellViewModel(MakeScanData(), pending, new Fakes.FakeRegistryService());
        var search = vm.TaskbarChoiceSettings[0];

        search.SelectedOption = search.Options.First(o => o.Value == 0); // Hidden
        await WaitForStagingAsync(pending, 1);

        var change = pending.PendingGroups[0].Changes[0];
        Assert.Equal("taskbar-search-mode", change.SettingId);
        Assert.Equal("3", change.BeforeValue);
        Assert.Equal("0", change.AfterValue);
        Assert.True(search.HasPendingChange);
    }

    [Fact]
    public async Task Returning_to_the_registry_value_unstages()
    {
        var pending = new PendingChangesService();
        var vm = new ShellViewModel(MakeScanData(), pending, new Fakes.FakeRegistryService());
        var search = vm.TaskbarChoiceSettings[0];

        search.SelectedOption = search.Options.First(o => o.Value == 1);
        await WaitForStagingAsync(pending, 1);

        search.SelectedOption = search.Options.First(o => o.Value == 3);
        await WaitForStagingAsync(pending, 0);
        Assert.False(search.HasPendingChange);
    }

    [Fact]
    public async Task Discarding_resets_the_selection_to_registry_state()
    {
        var pending = new PendingChangesService();
        var vm = new ShellViewModel(MakeScanData(), pending, new Fakes.FakeRegistryService());
        var combining = vm.TaskbarChoiceSettings[1];

        combining.SelectedOption = combining.Options.First(o => o.Value == 2); // Never
        await WaitForStagingAsync(pending, 1);

        pending.Unstage(pending.PendingGroups[0].GroupId);
        for (var i = 0; i < 100 && combining.HasPendingChange; i++)
            await Task.Delay(50);

        Assert.False(combining.HasPendingChange);
        Assert.Equal(0, combining.SelectedOption?.Value);
    }
}
