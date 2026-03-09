using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class ContextMenuHandlerViewModelTests : IDisposable
{
    private readonly IPendingChangesService _pendingService = new PendingChangesService();
    private readonly List<ContextMenuHandlerViewModel> _disposables = [];

    private ContextMenuHandlerViewModel CreateVm(
        HandlerClassification classification = HandlerClassification.ThirdParty,
        bool isEnabled = true,
        IReadOnlyList<string>? allScopes = null,
        IReadOnlyList<string>? allRegistryPaths = null,
        string name = "TestHandler",
        string clsid = "{12345678-1234-1234-1234-123456789ABC}",
        string? publisher = "TestPublisher",
        DisableMethod disableMethod = DisableMethod.None)
    {
        var handler = new ContextMenuHandler(
            Name: name,
            Clsid: clsid,
            RegistryPath: @"HKCR\*\shellex\ContextMenuHandlers\TestHandler",
            AppliesTo: "All files",
            DllPath: @"C:\test.dll",
            Publisher: publisher,
            IsEnabled: isEnabled,
            Classification: classification,
            AllRegistryPaths: allRegistryPaths,
            AllScopes: allScopes,
            DisableMethod: disableMethod);

        var vm = new ContextMenuHandlerViewModel(handler, _pendingService);
        _disposables.Add(vm);
        return vm;
    }

    [Fact]
    public void Constructor_sets_label_from_handler_name()
    {
        var vm = CreateVm(name: "7-Zip Shell Extension");
        Assert.Equal("7-Zip Shell Extension", vm.Label);
    }

    [Fact]
    public void Constructor_sets_classification()
    {
        var vm = CreateVm(classification: HandlerClassification.Critical);
        Assert.Equal(HandlerClassification.Critical, vm.Classification);
    }

    [Fact]
    public void Constructor_sets_IsEnabled_from_handler()
    {
        var vm = CreateVm(isEnabled: false);
        Assert.False(vm.IsEnabled);
    }

    [Fact]
    public void Description_for_critical_shows_critical_text()
    {
        var vm = CreateVm(classification: HandlerClassification.Critical);
        Assert.Contains("Windows built-in (critical)", vm.Description);
    }

    [Fact]
    public void Description_for_system_shows_publisher()
    {
        var vm = CreateVm(classification: HandlerClassification.System, publisher: "Microsoft Corporation");
        Assert.Contains("Windows built-in", vm.Description);
        Assert.Contains("Microsoft Corporation", vm.Description);
    }

    [Fact]
    public void Description_for_optional_shows_optional_text()
    {
        var vm = CreateVm(classification: HandlerClassification.Optional, publisher: "Microsoft Corporation");
        Assert.Contains("Microsoft (optional)", vm.Description);
    }

    [Fact]
    public void Description_for_thirdparty_shows_publisher_only()
    {
        var vm = CreateVm(classification: HandlerClassification.ThirdParty, publisher: "Igor Pavlov");
        Assert.Equal("Igor Pavlov", vm.Description);
    }

    [Fact]
    public void SetScopeNote_updates_description_with_scope_text()
    {
        var vm = CreateVm(classification: HandlerClassification.ThirdParty, publisher: "Igor Pavlov");
        vm.SetScopeNote("appears in: File, Folders");

        Assert.Contains("appears in: File, Folders", vm.Description);
        Assert.Contains("Igor Pavlov", vm.Description);
    }

    [Fact]
    public void WarningText_for_critical_contains_warning()
    {
        var vm = CreateVm(classification: HandlerClassification.Critical, name: "Open With");
        Assert.Contains("Disabling removes", vm.WarningText);
        Assert.Contains("Open With", vm.WarningText);
    }

    [Fact]
    public void WarningText_for_system_contains_mild_warning()
    {
        var vm = CreateVm(classification: HandlerClassification.System);
        Assert.Contains("Windows feature", vm.WarningText);
    }

    [Fact]
    public void WarningText_for_thirdparty_is_empty()
    {
        var vm = CreateVm(classification: HandlerClassification.ThirdParty);
        Assert.Empty(vm.WarningText);
    }

    [Fact]
    public async Task Toggle_stages_change_after_debounce()
    {
        var vm = CreateVm(isEnabled: true);
        vm.IsEnabled = false;

        await Task.Delay(350);

        Assert.True(_pendingService.PendingCount > 0);
    }

    [Fact]
    public async Task Toggle_back_unstages_change()
    {
        var vm = CreateVm(isEnabled: true);
        vm.IsEnabled = false;

        await Task.Delay(350);
        Assert.True(_pendingService.PendingCount > 0);

        vm.IsEnabled = true;

        await Task.Delay(350);
        Assert.Equal(0, _pendingService.PendingCount);
    }

    [Fact]
    public void Initial_state_no_pending()
    {
        var vm = CreateVm();
        Assert.False(vm.HasPendingChange);
        Assert.False(vm.IsPendingEnable);
        Assert.False(vm.IsPendingDisable);
    }

    [Fact]
    public void AllScopes_populated_from_handler()
    {
        var vm = CreateVm(allScopes: ["All files", "Directories"]);
        Assert.Equal(2, vm.AllScopes.Count);
    }

    [Fact]
    public void Clsid_populated_from_handler()
    {
        var vm = CreateVm(clsid: "{AAAA-BBBB}");
        Assert.Equal("{AAAA-BBBB}", vm.Clsid);
    }

    [Fact]
    public void CanMigrate_true_when_DisableMethod_is_DashPrefix()
    {
        var vm = CreateVm(isEnabled: false, disableMethod: DisableMethod.DashPrefix);
        Assert.True(vm.CanMigrate);
    }

    [Fact]
    public void CanMigrate_false_when_DisableMethod_is_None()
    {
        var vm = CreateVm(disableMethod: DisableMethod.None);
        Assert.False(vm.CanMigrate);
    }

    [Fact]
    public void CanMigrate_false_when_DisableMethod_is_BlockedList()
    {
        var vm = CreateVm(isEnabled: false, disableMethod: DisableMethod.BlockedList);
        Assert.False(vm.CanMigrate);
    }

    [Fact]
    public void CanMigrate_false_when_DisableMethod_is_Both()
    {
        var vm = CreateVm(isEnabled: false, disableMethod: DisableMethod.Both);
        Assert.False(vm.CanMigrate);
    }

    [Fact]
    public void DisableMethodText_shows_legacy_for_DashPrefix()
    {
        var vm = CreateVm(isEnabled: false, disableMethod: DisableMethod.DashPrefix);
        Assert.Contains("dash-prefix", vm.DisableMethodText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("legacy", vm.DisableMethodText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisableMethodText_empty_for_None()
    {
        var vm = CreateVm(disableMethod: DisableMethod.None);
        Assert.Empty(vm.DisableMethodText);
    }

    [Fact]
    public void DisableMethodText_shows_blocked_list_for_BlockedList()
    {
        var vm = CreateVm(isEnabled: false, disableMethod: DisableMethod.BlockedList);
        Assert.Contains("Blocked List", vm.DisableMethodText);
    }

    [Fact]
    public void MigrateCommand_stages_migration_group_when_CanMigrate()
    {
        var vm = CreateVm(
            isEnabled: false,
            disableMethod: DisableMethod.DashPrefix,
            allRegistryPaths: [@"HKCR\*\shellex\ContextMenuHandlers\TestHandler"]);

        vm.MigrateCommand.Execute(null);

        Assert.True(_pendingService.PendingCount > 0);
        var group = _pendingService.PendingGroups[0];
        Assert.Contains("Migrate", group.DisplayName);
        Assert.Contains(group.Changes, c => c.SystemLocation.Contains("Blocked"));
        Assert.False(vm.CanMigrate);
    }

    [Fact]
    public void MigrateCommand_second_click_does_not_double_stage()
    {
        var vm = CreateVm(
            isEnabled: false,
            disableMethod: DisableMethod.DashPrefix,
            allRegistryPaths: [@"HKCR\*\shellex\ContextMenuHandlers\TestHandler"]);

        vm.MigrateCommand.Execute(null);
        vm.MigrateCommand.Execute(null);

        Assert.Equal(1, _pendingService.PendingCount);
    }

    [Fact]
    public void MigrateCommand_does_nothing_when_not_CanMigrate()
    {
        var vm = CreateVm(disableMethod: DisableMethod.None);

        vm.MigrateCommand.Execute(null);

        Assert.Equal(0, _pendingService.PendingCount);
    }

    public void Dispose()
    {
        foreach (var vm in _disposables)
            vm.Dispose();
    }
}
