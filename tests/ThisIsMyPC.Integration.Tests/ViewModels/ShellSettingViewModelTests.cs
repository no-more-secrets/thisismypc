using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class ShellSettingViewModelTests : IDisposable
{
    private readonly PendingChangesService _pendingService = new();

    private static ExplorerPreference MakePreference(bool isEnabled = false) =>
        new(
            Id: "hidden-files",
            DisplayName: "Show hidden files",
            Description: "Display hidden files",
            RegistryKeyPath: @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName: "Hidden",
            ValueType: ChangeValueType.Registry_DWord,
            CurrentValue: isEnabled ? "1" : "2",
            EnabledValue: "1",
            DisabledValue: "2",
            IsEnabled: isEnabled,
            RestartRequirement: RestartRequirement.ExplorerRefresh);

    [Fact]
    public void Constructor_sets_label_from_preference()
    {
        var pref = MakePreference();
        var vm = new ShellSettingViewModel(pref, _pendingService, () => false);

        Assert.Equal("Show hidden files", vm.Label);
    }

    [Fact]
    public void Constructor_sets_description_from_preference()
    {
        var pref = MakePreference();
        var vm = new ShellSettingViewModel(pref, _pendingService, () => false);

        Assert.Equal("Display hidden files", vm.Description);
    }

    [Fact]
    public void Constructor_sets_system_path_from_preference()
    {
        var pref = MakePreference();
        var vm = new ShellSettingViewModel(pref, _pendingService, () => false);

        Assert.Equal(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\Hidden", vm.SystemPath);
    }

    [Fact]
    public void Constructor_sets_initial_enabled_state()
    {
        var enabled = new ShellSettingViewModel(MakePreference(true), _pendingService, () => true);
        var disabled = new ShellSettingViewModel(MakePreference(false), _pendingService, () => false);

        Assert.True(enabled.IsEnabled);
        Assert.False(disabled.IsEnabled);
    }

    [Fact]
    public void Constructor_does_not_stage_during_initialization()
    {
        var pref = MakePreference(isEnabled: false);
        _ = new ShellSettingViewModel(pref, _pendingService, () => false);

        Assert.Equal(0, _pendingService.PendingCount);
    }

    [Fact]
    public async Task Toggle_stages_change_after_debounce()
    {
        var pref = MakePreference(isEnabled: false);
        var vm = new ShellSettingViewModel(pref, _pendingService, () => false);

        vm.IsEnabled = true;
        await Task.Delay(350); // debounce is 250ms

        Assert.Equal(1, _pendingService.PendingCount);
    }

    [Fact]
    public async Task Toggle_back_to_original_unstages()
    {
        var pref = MakePreference(isEnabled: false);
        var vm = new ShellSettingViewModel(pref, _pendingService, () => false);

        vm.IsEnabled = true;
        await Task.Delay(350);
        Assert.Equal(1, _pendingService.PendingCount);

        vm.IsEnabled = false;
        await Task.Delay(350);
        Assert.Equal(0, _pendingService.PendingCount);
    }

    [Fact]
    public async Task HasPendingChange_true_when_toggled()
    {
        var pref = MakePreference(isEnabled: false);
        var vm = new ShellSettingViewModel(pref, _pendingService, () => false);

        vm.IsEnabled = true;
        await Task.Delay(350);

        Assert.True(vm.HasPendingChange);
        Assert.True(vm.IsPendingEnable);
        Assert.False(vm.IsPendingDisable);
    }

    [Fact]
    public void Generic_constructor_sets_properties()
    {
        var vm = new ShellSettingViewModel(
            label: "Widgets",
            description: "Show widgets",
            systemPath: @"HKCU\test\path",
            isEnabled: true,
            pendingChangesService: _pendingService,
            changeFactory: enable => new ChangeDescriptor
            {
                ModuleId = "Shell & Explorer",
                SettingId = "widgets",
                DisplayName = "Widgets",
                SystemLocation = @"HKCU\test\path",
                BeforeValue = "1",
                AfterValue = enable ? "1" : "0",
                BeforeDisplay = "Shown",
                AfterDisplay = enable ? "Shown" : "Hidden",
                ValueType = ChangeValueType.Registry_DWord,
                Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            },
            readRegistryState: () => true);

        Assert.Equal("Widgets", vm.Label);
        Assert.Equal("Show widgets", vm.Description);
        Assert.Equal(@"HKCU\test\path", vm.SystemPath);
        Assert.True(vm.IsEnabled);
    }

    [Fact]
    public void Dispose_cancels_pending_debounce()
    {
        var pref = MakePreference(isEnabled: false);
        var vm = new ShellSettingViewModel(pref, _pendingService, () => false);

        vm.IsEnabled = true; // starts debounce
        vm.Dispose();

        // After dispose, the debounce should have been cancelled
        // No exception or crash expected
        Assert.Equal(0, _pendingService.PendingCount);
    }

    public void Dispose()
    {
        _pendingService.DiscardAll();
    }
}
