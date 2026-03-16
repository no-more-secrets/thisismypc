using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Integration.Tests.Fakes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class ContextMenuViewModelOrphanTests : IDisposable
{
    private readonly PendingChangesService _pendingService = new();
    private readonly FakeRegistryService _registryService = new();
    private ContextMenuViewModel? _vm;

    private static ContextMenuHandler MakeHandler(
        string name = "Valid",
        string clsid = "{1111-AAAA}",
        bool isOrphaned = false,
        string? orphanReason = null) =>
        new(
            Name: name,
            Clsid: clsid,
            RegistryPath: $@"HKCR\*\shellex\ContextMenuHandlers\{name}",
            AppliesTo: "All files",
            DllPath: isOrphaned ? @"C:\missing\old.dll" : @"C:\Windows\System32\shell32.dll",
            Publisher: isOrphaned ? null : "Microsoft",
            IsEnabled: true,
            AllScopes: ["All files"],
            AllRegistryPaths: [$@"HKCR\*\shellex\ContextMenuHandlers\{name}"],
            IsOrphaned: isOrphaned,
            OrphanReason: orphanReason);

    private ContextMenuViewModel CreateVm(params ContextMenuHandler[] handlers)
    {
        _vm = new ContextMenuViewModel(handlers, _pendingService, _registryService);
        return _vm;
    }

    [Fact]
    public void OrphanCount_reflects_total_orphans()
    {
        var vm = CreateVm(
            MakeHandler("Valid1", "{1111}"),
            MakeHandler("Orphan1", "{2222}", isOrphaned: true, orphanReason: "DLL not found"),
            MakeHandler("Orphan2", "{3333}", isOrphaned: true, orphanReason: "DLL not found"));

        Assert.Equal(2, vm.OrphanCount);
    }

    [Fact]
    public void OrphanCount_zero_when_no_orphans()
    {
        var vm = CreateVm(
            MakeHandler("Valid1", "{1111}"),
            MakeHandler("Valid2", "{2222}"));

        Assert.Equal(0, vm.OrphanCount);
    }

    [Fact]
    public void IsOrphanFilterActive_defaults_false()
    {
        var vm = CreateVm(MakeHandler());
        Assert.False(vm.IsOrphanFilterActive);
    }

    [Fact]
    public void ToggleOrphanFilter_toggles_state()
    {
        var vm = CreateVm(MakeHandler());

        vm.ToggleOrphanFilterCommand.Execute(null);
        Assert.True(vm.IsOrphanFilterActive);

        vm.ToggleOrphanFilterCommand.Execute(null);
        Assert.False(vm.IsOrphanFilterActive);
    }

    [Fact]
    public void OrphanFilter_active_shows_only_orphaned_handlers()
    {
        var vm = CreateVm(
            MakeHandler("Valid1", "{1111}"),
            MakeHandler("Orphan1", "{2222}", isOrphaned: true, orphanReason: "DLL not found"));

        // Before filter: both handlers visible
        Assert.Equal(2, vm.FileHandlers.Count);

        vm.IsOrphanFilterActive = true;

        // After filter: only orphan visible
        Assert.Single(vm.FileHandlers);
        Assert.True(vm.FileHandlers[0].IsOrphaned);
    }

    [Fact]
    public void CleanUpAllOrphansCommand_enabled_when_orphans_exist()
    {
        var vm = CreateVm(
            MakeHandler("Orphan1", "{2222}", isOrphaned: true, orphanReason: "DLL not found"));

        Assert.True(vm.CleanUpAllOrphansCommand.CanExecute(null));
    }

    [Fact]
    public void CleanUpAllOrphansCommand_disabled_when_no_orphans()
    {
        var vm = CreateVm(MakeHandler("Valid1", "{1111}"));

        Assert.False(vm.CleanUpAllOrphansCommand.CanExecute(null));
    }

    [Fact]
    public void CleanUpAllOrphansCommand_stages_bulk_group()
    {
        var vm = CreateVm(
            MakeHandler("Valid1", "{1111}"),
            MakeHandler("Orphan1", "{2222}", isOrphaned: true, orphanReason: "DLL not found"),
            MakeHandler("Orphan2", "{3333}", isOrphaned: true, orphanReason: "DLL not found"));

        vm.CleanUpAllOrphansCommand.Execute(null);

        Assert.True(_pendingService.PendingCount > 0);
        var group = _pendingService.PendingGroups[0];
        Assert.Contains("2", group.DisplayName); // "Clean up 2 orphaned handlers"
    }

    public void Dispose()
    {
        _vm?.Dispose();
    }
}
