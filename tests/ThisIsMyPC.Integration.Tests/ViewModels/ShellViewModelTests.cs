using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class ShellViewModelTests
{
    private static readonly IReadOnlyList<ExplorerPreference> TestPreferences =
    [
        new ExplorerPreference(
            Id: "hidden-files", DisplayName: "Show hidden files", Description: "desc",
            RegistryKeyPath: @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName: "Hidden", ValueType: ChangeValueType.Registry_DWord,
            CurrentValue: "2", EnabledValue: "1", DisabledValue: "2",
            IsEnabled: false, RestartRequirement: RestartRequirement.ExplorerRefresh),
        new ExplorerPreference(
            Id: "file-extensions", DisplayName: "Show file extensions", Description: "desc",
            RegistryKeyPath: @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName: "HideFileExt", ValueType: ChangeValueType.Registry_DWord,
            CurrentValue: "1", EnabledValue: "0", DisabledValue: "1",
            IsEnabled: false, RestartRequirement: RestartRequirement.ExplorerRefresh),
    ];

    private static ShellScanData MakeScanData(
        IReadOnlyList<ExplorerPreference>? prefs = null,
        TaskbarSettings? taskbar = null) =>
        new(
            ExplorerPreferences: prefs ?? TestPreferences,
            Taskbar: taskbar ?? new TaskbarSettings(1, true, false, false));

    [Fact]
    public void Constructor_creates_correct_number_of_explorer_settings()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var vm = new ShellViewModel(MakeScanData(), pendingService, registryService);

        Assert.Equal(2, vm.FileExplorerSettings.Count);
    }

    [Fact]
    public void Constructor_creates_three_taskbar_settings()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var vm = new ShellViewModel(MakeScanData(), pendingService, registryService);

        Assert.Equal(2, vm.TaskbarSettings.Count);
    }

    [Fact]
    public void Explorer_settings_have_correct_labels()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var vm = new ShellViewModel(MakeScanData(), pendingService, registryService);

        Assert.Equal("Show hidden files", vm.FileExplorerSettings[0].Label);
        Assert.Equal("Show file extensions", vm.FileExplorerSettings[1].Label);
    }

    [Fact]
    public void Explorer_settings_reflect_scan_enabled_state()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var vm = new ShellViewModel(MakeScanData(), pendingService, registryService);

        Assert.False(vm.FileExplorerSettings[0].IsEnabled); // Hidden=2 means not showing
        Assert.False(vm.FileExplorerSettings[1].IsEnabled); // HideFileExt=1 means hiding
    }

    [Fact]
    public void Taskbar_alignment_setting_label_is_correct()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var vm = new ShellViewModel(MakeScanData(), pendingService, registryService);

        Assert.Equal("Taskbar alignment (Left)", vm.TaskbarSettings[0].Label);
    }

    [Fact]
    public void Taskbar_alignment_enabled_when_left_aligned()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var scanData = MakeScanData(taskbar: new TaskbarSettings(0, true, false, false));
        var vm = new ShellViewModel(scanData, pendingService, registryService);

        Assert.True(vm.TaskbarSettings[0].IsEnabled); // alignment 0 = left
    }

    [Fact]
    public void Taskbar_widgets_reflects_scan_state()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var scanData = MakeScanData(taskbar: new TaskbarSettings(1, false, false, false));
        var vm = new ShellViewModel(scanData, pendingService, registryService);

        Assert.False(vm.TaskbarSettings[1].IsEnabled); // widgets disabled
    }

    [Fact]
    public void Classic_context_menu_reflects_scan_state()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var scanData = MakeScanData(taskbar: new TaskbarSettings(1, true, true, false));
        var vm = new ShellViewModel(scanData, pendingService, registryService);

        // Classic context menu leads the General tab (one of the most-wanted Win11 changes).
        Assert.Equal("Classic context menu", vm.GeneralSettings[0].Label);
        Assert.True(vm.GeneralSettings[0].IsEnabled); // classic menu enabled
    }

    [Fact]
    public void Empty_preferences_produces_no_native_file_explorer_rows()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var scanData = MakeScanData(prefs: []);
        var vm = new ShellViewModel(scanData, pendingService, registryService);

        Assert.Empty(vm.FileExplorerSettings);
    }

    [Fact]
    public void Control_interface_choice_appears_with_ExplorerPatcher()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var controlInterface = ExplorerPatcherCatalog.Entries.First(s => s.RegistryValueName == "FileExplorerCommandUI")
            with { CurrentValue = 4 };
        var scan = MakeScanData() with
        {
            ExplorerPatcherInstalled = true,
            ExplorerPatcherSettings = [controlInterface],
        };
        var vm = new ShellViewModel(scan, pendingService, registryService);

        var choice = Assert.Single(vm.FileExplorerPatcherGroups).Choices.Single();
        Assert.Equal("Control Interface", choice.Label);
        Assert.Equal([0, 4, 1, 2], choice.Options.Select(option => option.Value));
        Assert.Equal(4, choice.SelectedOption?.Value);
    }

    [Fact]
    public void Control_interface_choice_is_hidden_without_ExplorerPatcher()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var setting = ExplorerPatcherCatalog.Entries.First(s => s.RegistryValueName == "FileExplorerCommandUI");
        var scanData = MakeScanData() with { ExplorerPatcherSettings = [setting] };
        var vm = new ShellViewModel(scanData, pendingService, registryService);

        Assert.False(vm.ShowFileExplorerPatcher);
    }
}
