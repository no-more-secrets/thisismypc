using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

/// <summary>
/// The window under simulated mode: Apply is refused before any module is
/// called, the banner names what is simulated, Home shows the pretend device,
/// and Reset puts everything back.
/// </summary>
public sealed class MainWindowViewModelSimulationTests
{
    private static (MainWindowViewModel Vm, PendingChangesService Pending, DebugSimulation Simulation, List<string> Applied) Create()
    {
        var applied = new List<string>();
        var module = new Fakes.FakeModule("TestModule", change =>
        {
            applied.Add(change.SettingId);
            return Task.FromResult(OperationResult<bool>.Success(true));
        });
        var pending = new PendingChangesService();
        var simulation = new DebugSimulation();
        var vm = new MainWindowViewModel(
            new NavigationService([module]), pending, new Fakes.FakeChangeHistoryService(), new Fakes.FakeRegistryService(),
            new Fakes.FakeExplorerRestartService(),
            new ReviewPanelViewModel(pending, new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-sim-{Guid.NewGuid():N}"))),
            new Fakes.FakeSetProvider(), [],
            new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-sim-{Guid.NewGuid():N}")),
            new Fakes.FakeRestorePointService(),
            debugSimulation: simulation);
        return (vm, pending, simulation, applied);
    }

    private static ChangeDescriptor Change() => new()
    {
        ModuleId = "TestModule",
        SettingId = "s1",
        DisplayName = "Test Setting",
        SystemLocation = @"HKLM\Test",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Off",
        AfterDisplay = "On",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Enable,
    };

    [Fact]
    public async Task Apply_IsRefusedWhileSimulating_AndWorksAgainAfterReset()
    {
        var (vm, pending, simulation, applied) = Create();
        await vm.InitializeAsync();
        pending.Stage(Change());
        simulation.Sku = WindowsSku.Enterprise;
        Assert.True(vm.IsSimulationActive);
        Assert.Contains("Windows 11 Enterprise", vm.SimulationBannerText, StringComparison.Ordinal);
        Assert.Contains("Apply, installs, and service actions are off", vm.SimulationBannerText, StringComparison.Ordinal);

        await vm.ApplyAllCommand.ExecuteAsync(null);

        Assert.Empty(applied);
        Assert.Equal(1, pending.PendingCount);
        Assert.Equal(StatusSeverity.Error, vm.StatusSeverity);
        Assert.Equal(DebugSimulation.BlockedMessage, vm.StatusMessage);
        Assert.False(vm.IsApplying);

        vm.ResetSimulationCommand.Execute(null);
        Assert.False(vm.IsSimulationActive);
        Assert.Equal(string.Empty, vm.SimulationBannerText);

        await vm.ApplyAllCommand.ExecuteAsync(null);
        Assert.Equal(["s1"], applied);
        Assert.Equal(0, pending.PendingCount);
    }

    [Fact]
    public async Task Home_ShowsTheSimulatedDeviceAndEdition_AndTheRealOnesAfterReset()
    {
        var (vm, _, simulation, _) = Create();
        await vm.InitializeAsync();
        var real = Assert.IsType<HomeViewModel>(vm.CurrentContent).Identity;

        simulation.Device = DebugSimulation.DevicePresets.Single(p => p.DisplayName == "Lenovo laptop");
        simulation.Sku = WindowsSku.Home;

        // The open page is rebuilt with the simulated identity.
        var simulated = Assert.IsType<HomeViewModel>(vm.CurrentContent).Identity;
        Assert.Equal("LENOVO", simulated.Manufacturer);
        Assert.Equal("Laptop (simulated)", simulated.SystemType);
        Assert.Equal("Windows 11 Home (simulated)", simulated.WindowsEdition);
        Assert.Equal(real.MachineName, simulated.MachineName);

        simulation.Reset();
        var restored = Assert.IsType<HomeViewModel>(vm.CurrentContent).Identity;
        Assert.Equal(real.Manufacturer, restored.Manufacturer);
        Assert.Equal(real.WindowsEdition, restored.WindowsEdition);
        Assert.Equal(real.SystemType, restored.SystemType);
    }

    /// <summary>A history service that counts routing calls, so a refused undo proves the service was never reached.</summary>
    private sealed class CountingHistory : IChangeHistoryService
    {
        public int RevertCalls { get; private set; }
        public int RedoCalls { get; private set; }
        public Task InitializeAsync() => Task.CompletedTask;
        public Task RecordChangesAsync(MutationResult result) => Task.CompletedTask;
        public Task RecordDriftEventsAsync(IReadOnlyList<ChangeHistoryEntry> driftEntries) => Task.CompletedTask;
        public Task<IReadOnlyList<ChangeHistoryEntry>> GetHistoryAsync(int? limit = null, int? offset = null) => Task.FromResult<IReadOnlyList<ChangeHistoryEntry>>([]);
        public Task<IReadOnlyList<ChangeHistoryEntry>> GetRecentGroupedAsync(int groupLimit = 50) => Task.FromResult<IReadOnlyList<ChangeHistoryEntry>>([]);
        public Task<int> GetGroupCountAsync() => Task.FromResult(0);
        public Task<OperationResult<bool>> RevertChangeAsync(long historyId, Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc)
        {
            RevertCalls++;
            return Task.FromResult(OperationResult<bool>.Success(true));
        }
        public Task<OperationResult<bool>> RedoChangeAsync(long historyId, Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc)
        {
            RedoCalls++;
            return Task.FromResult(OperationResult<bool>.Success(true));
        }
        public Task<int> GetEntryCountAsync() => Task.FromResult(0);
        public Task ClearHistoryAsync() => Task.CompletedTask;
    }

    private static ChangeHistoryEntryViewModel Entry() => new()
    {
        Id = 1,
        DisplayName = "Test Setting",
        ModuleId = "TestModule",
        SystemLocation = @"HKLM\Test",
        BeforeDisplay = "Off",
        AfterDisplay = "On",
        Category = ChangeCategory.Enable,
        AppliedAt = DateTimeOffset.UtcNow,
        IsReverted = false,
    };

    [Fact]
    public async Task HistoryUndoAndRedo_AreRefusedWhileSimulating_BeforeTheServiceRoutes()
    {
        var history = new CountingHistory();
        var pending = new PendingChangesService();
        var simulation = new DebugSimulation();
        var explorer = new Fakes.FakeExplorerRestartService();
        var vm = new MainWindowViewModel(
            new NavigationService([new Fakes.FakeModule("TestModule")]), pending, history, new Fakes.FakeRegistryService(), explorer,
            new ReviewPanelViewModel(pending, new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-sim-{Guid.NewGuid():N}"))),
            new Fakes.FakeSetProvider(), [],
            new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-sim-{Guid.NewGuid():N}")),
            new Fakes.FakeRestorePointService(),
            debugSimulation: simulation);
        simulation.OwnerModeState = OwnerModeState.Running;

        await vm.ChangeHistory.RestoreCommand.ExecuteAsync(Entry());
        await vm.ChangeHistory.RedoCommand.ExecuteAsync(Entry());

        Assert.Equal(0, history.RevertCalls);
        Assert.Equal(0, history.RedoCalls);
        Assert.Equal(DebugSimulation.BlockedMessage, vm.ChangeHistory.ErrorMessage);

        // Explorer restart is a real mutation too.
        await vm.RestartExplorerCommand.ExecuteAsync(null);
        Assert.False(explorer.WasCalled);
        Assert.Equal(DebugSimulation.BlockedMessage, vm.StatusMessage);

        simulation.Reset();
        await vm.ChangeHistory.RestoreCommand.ExecuteAsync(Entry());
        await vm.ChangeHistory.RedoCommand.ExecuteAsync(Entry());
        Assert.Equal(1, history.RevertCalls);
        Assert.Equal(1, history.RedoCalls);
        await vm.RestartExplorerCommand.ExecuteAsync(null);
        Assert.True(explorer.WasCalled);
    }

    [Fact]
    public async Task Simulation_CannotSwitchOnWhileAnApplyIsInFlight()
    {
        var gate = new TaskCompletionSource<OperationResult<bool>>();
        var applied = new List<string>();
        var module = new Fakes.FakeModule("TestModule", change =>
        {
            applied.Add(change.SettingId);
            return gate.Task;
        });
        var pending = new PendingChangesService();
        var simulation = new DebugSimulation();
        var refusals = 0;
        simulation.Changed += (_, _) => { if (simulation.LastRefusal is not null) refusals++; };
        var vm = new MainWindowViewModel(
            new NavigationService([module]), pending, new Fakes.FakeChangeHistoryService(), new Fakes.FakeRegistryService(),
            new Fakes.FakeExplorerRestartService(),
            new ReviewPanelViewModel(pending, new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-sim-{Guid.NewGuid():N}"))),
            new Fakes.FakeSetProvider(), [],
            new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-sim-{Guid.NewGuid():N}")),
            new Fakes.FakeRestorePointService(),
            debugSimulation: simulation);
        await vm.InitializeAsync();
        pending.Stage(Change());

        // The batch is parked inside the module call: the lease is held.
        var apply = vm.ApplyAllCommand.ExecuteAsync(null);
        Assert.Single(applied);
        Assert.True(simulation.IsMutationInFlight);

        simulation.Sku = WindowsSku.Home;
        simulation.SetCapability(SystemCapability.OpenRgb, true);
        simulation.Reset();
        Assert.False(simulation.IsActive);
        Assert.Equal(DebugSimulation.InFlightMessage, simulation.LastRefusal);
        Assert.Equal(3, refusals);
        Assert.False(vm.IsSimulationActive);

        gate.SetResult(OperationResult<bool>.Success(true));
        await apply;
        Assert.False(simulation.IsMutationInFlight);
        Assert.Equal(0, pending.PendingCount);

        // Released: the same change goes through and the refusal clears.
        simulation.Sku = WindowsSku.Home;
        Assert.True(simulation.IsActive);
        Assert.Null(simulation.LastRefusal);
        Assert.True(vm.IsSimulationActive);
    }

    [Fact]
    public async Task Staging_IsRefusedAndContentDisabled_WhileAGroupIsUnresolved()
    {
        var pending = new PendingChangesService();
        var module = new Fakes.FakeModule("TestModule", _ =>
            Task.FromResult(OperationResult<bool>.Failure("Registry access denied", ErrorCategory.AccessDenied)));
        var vm = new MainWindowViewModel(
            new NavigationService([module]), pending, new Fakes.FakeChangeHistoryService(), new Fakes.FakeRegistryService(),
            new Fakes.FakeExplorerRestartService(),
            new ReviewPanelViewModel(pending, new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-sim-{Guid.NewGuid():N}"))),
            new Fakes.FakeSetProvider(), [],
            new Core.Sets.CustomSetWriter(Path.Combine(Path.GetTempPath(), $"tipc-sim-{Guid.NewGuid():N}")),
            new Fakes.FakeRestorePointService());
        await vm.InitializeAsync();
        Assert.True(vm.IsContentInteractive);
        pending.Stage(Change());
        await vm.ApplyAllCommand.ExecuteAsync(null);
        Assert.True(vm.HasUnresolvedGroups);
        Assert.False(vm.IsContentInteractive);

        // Nothing new may stage on top of the unresolved group.
        vm.StageDebugChange(ChangeCategory.Enable);
        Assert.Equal(1, pending.PendingCount);
        Assert.Equal(MainWindowViewModel.StagingBlockedMessage, vm.StatusMessage);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.DiscardAllCommand).ExecuteAsync(null);
        Assert.True(vm.IsContentInteractive);
        vm.StageDebugChange(ChangeCategory.Enable);
        Assert.Equal(1, pending.PendingCount);
    }

    [Fact]
    public void StageDebugChange_StagesAgainstTheSampleModule()
    {
        var (vm, pending, _, _) = Create();

        vm.StageDebugChange(ChangeCategory.Disable);
        vm.StageDebugChange(ChangeCategory.Modify);

        Assert.Equal(2, pending.PendingCount);
        var first = pending.PendingGroups[0].Changes[0];
        Assert.Equal("DebugModule", first.ModuleId);
        Assert.Equal(ChangeCategory.Disable, first.Category);
        Assert.Equal("Enabled", first.BeforeDisplay);
        Assert.Equal("Value B", pending.PendingGroups[1].Changes[0].AfterDisplay);
    }
}
