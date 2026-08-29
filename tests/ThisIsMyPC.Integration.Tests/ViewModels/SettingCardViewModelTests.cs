using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class SettingCardViewModelTests
{
    private readonly PendingChangesService _pendingService = new();
    private bool _liveState;

    private SettingCardViewModel CreateVm(bool initiallyEnabled = false)
    {
        _liveState = initiallyEnabled;
        var source = new SettingCardSource
        {
            Model = new SettingCardModel
            {
                SettingId = "test-card",
                ModuleId = "Test",
                DisplayName = "Test card",
                Description = "A test setting.",
                ControlType = SettingControlType.Toggle,
                CurrentValue = initiallyEnabled ? "1" : "0",
                RegistryPath = @"HKCU\Software\Test",
                ValueName = "Value",
                RegistryValueType = "Registry_DWord",
                GroupId = "Group A",
            },
            CreateToggleGroup = desired => new ChangeGroup
            {
                GroupId = Guid.NewGuid().ToString("N"),
                DisplayName = "Test card",
                Description = "Test card",
                Changes =
                [
                    new ChangeDescriptor
                    {
                        ModuleId = "Test",
                        SettingId = "test-card",
                        DisplayName = "Test card",
                        SystemLocation = @"HKCU\Software\Test\Value",
                        BeforeValue = _liveState ? "1" : "0",
                        AfterValue = desired ? "1" : "0",
                        BeforeDisplay = _liveState ? "On" : "Off",
                        AfterDisplay = desired ? "On" : "Off",
                        ValueType = ChangeValueType.Registry_DWord,
                    },
                ],
            },
            ReadCurrentState = () => _liveState,
        };
        return new SettingCardViewModel(source, _pendingService);
    }

    [Fact]
    public void Constructor_ExposesModelData_WithoutStaging()
    {
        var vm = CreateVm();

        Assert.Equal("Test card", vm.DisplayName);
        Assert.Equal("A test setting.", vm.Description);
        Assert.Equal(@"HKCU\Software\Test\Value", vm.SystemPath);
        Assert.True(vm.IsToggle);
        Assert.False(vm.IsEnabled);
        Assert.Equal(0, _pendingService.PendingCount);
        Assert.True(vm.IsDescriptionVisible);
        Assert.False(vm.IsRegistryDataVisible);
    }

    [Fact]
    public async Task Toggle_StagesAfterDebounce_AndSetsPendingTint()
    {
        var vm = CreateVm();

        vm.IsEnabled = true;
        await Task.Delay(350); // debounce is 250ms

        Assert.Equal(1, _pendingService.PendingCount);
        Assert.True(vm.HasPendingChange);
        Assert.True(vm.IsPendingEnable);
        Assert.False(vm.IsPendingDisable);
    }

    [Fact]
    public async Task ToggleBack_Unstages()
    {
        var vm = CreateVm();

        vm.IsEnabled = true;
        await Task.Delay(350);
        vm.IsEnabled = false;
        await Task.Delay(350);

        Assert.Equal(0, _pendingService.PendingCount);
        Assert.False(vm.HasPendingChange);
    }

    [Fact]
    public async Task DisableToggle_ShowsPendingDisableTint()
    {
        var vm = CreateVm(initiallyEnabled: true);

        vm.IsEnabled = false;
        await Task.Delay(350);

        Assert.True(vm.IsPendingDisable);
        Assert.False(vm.IsPendingEnable);
    }

    [Fact]
    public async Task DiscardAll_RevertsToggleToRegistryState()
    {
        var vm = CreateVm();
        vm.IsEnabled = true;
        await Task.Delay(350);

        _pendingService.DiscardAll();
        await Task.Delay(50);

        Assert.False(vm.IsEnabled);
        Assert.False(vm.HasPendingChange);
    }

    [Fact]
    public async Task StagedGroup_UsesLiveStateForBeforeValue()
    {
        var vm = CreateVm();

        // Live state changed outside the app between scan and toggle.
        _liveState = true;
        vm.IsEnabled = true;
        await Task.Delay(350);

        // Desired == live → nothing staged (no cosmetic no-op changes).
        Assert.Equal(0, _pendingService.PendingCount);
    }

    [Fact]
    public async Task RapidToggling_OnlyFinalStateStaged()
    {
        var vm = CreateVm();

        vm.IsEnabled = true;
        vm.IsEnabled = false;
        vm.IsEnabled = true;
        await Task.Delay(350);

        Assert.Equal(1, _pendingService.PendingCount);
        var change = _pendingService.PendingGroups.Single().Changes.Single();
        Assert.Equal("1", change.AfterValue);
    }

    [Fact]
    public async Task Dispose_StopsReactingToPendingChanges()
    {
        var vm = CreateVm();
        vm.IsEnabled = true;
        await Task.Delay(350);

        vm.Dispose();
        _pendingService.DiscardAll();
        await Task.Delay(50);

        // Disposed VM no longer reverts its toggle.
        Assert.True(vm.IsEnabled);
    }
}
