using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

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

        Assert.Equal(3, vm.ExplorerSettings.Count); // 2 preferences + command bar toggle
    }

    [Fact]
    public void Constructor_creates_three_taskbar_settings()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var vm = new ShellViewModel(MakeScanData(), pendingService, registryService);

        Assert.Equal(3, vm.TaskbarSettings.Count);
    }

    [Fact]
    public void Explorer_settings_have_correct_labels()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var vm = new ShellViewModel(MakeScanData(), pendingService, registryService);

        Assert.Equal("Show hidden files", vm.ExplorerSettings[0].Label);
        Assert.Equal("Show file extensions", vm.ExplorerSettings[1].Label);
    }

    [Fact]
    public void Explorer_settings_reflect_scan_enabled_state()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var vm = new ShellViewModel(MakeScanData(), pendingService, registryService);

        Assert.False(vm.ExplorerSettings[0].IsEnabled); // Hidden=2 means not showing
        Assert.False(vm.ExplorerSettings[1].IsEnabled); // HideFileExt=1 means hiding
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

        Assert.True(vm.TaskbarSettings[2].IsEnabled); // classic menu enabled
    }

    [Fact]
    public void Empty_preferences_produces_command_bar_toggle_only()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var scanData = MakeScanData(prefs: []);
        var vm = new ShellViewModel(scanData, pendingService, registryService);

        Assert.Single(vm.ExplorerSettings); // command bar toggle is always present
        Assert.Equal("Use classic command bar", vm.ExplorerSettings[0].Label);
    }

    [Fact]
    public void Command_bar_toggle_appears_in_explorer_settings()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var vm = new ShellViewModel(MakeScanData(), pendingService, registryService);

        var commandBarToggle = vm.ExplorerSettings[^1]; // last explorer setting
        Assert.Equal("Use classic command bar", commandBarToggle.Label);
        Assert.Contains("classic", commandBarToggle.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Command_bar_toggle_reflects_scan_state_enabled()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var scanData = MakeScanData(taskbar: new TaskbarSettings(1, true, false, true));
        var vm = new ShellViewModel(scanData, pendingService, registryService);

        Assert.True(vm.ExplorerSettings[^1].IsEnabled); // classic command bar enabled
    }

    [Fact]
    public void Command_bar_toggle_reflects_scan_state_disabled()
    {
        var pendingService = new PendingChangesService();
        var registryService = new Fakes.FakeRegistryService();
        var scanData = MakeScanData(taskbar: new TaskbarSettings(1, true, false, false));
        var vm = new ShellViewModel(scanData, pendingService, registryService);

        Assert.False(vm.ExplorerSettings[^1].IsEnabled); // modern command bar (default)
    }
}
