using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Annoyances;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// Discard All after a group was left in an unknown state: the open page is
/// scanned again from the module before it can stage anything, and a clean
/// discard does not rescan. Page loads hop through the dispatcher, so this
/// runs on the headless loop; no view is shown and nothing touches the live
/// system. CI-safe.
/// </summary>
public class DiscardRescanTests
{
    private const string ModuleId = "Windows Annoyances";

    /// <summary>
    /// The in-memory registry behind the real Annoyances module (the window
    /// builds the page only for the module types it knows). Every DWORD write
    /// is logged; writes to <see cref="RefusedValueName"/> are refused with
    /// access denied. Reads of the lock-screen overlay value count as scans:
    /// the module reads it once per scan and the page never reads it again.
    /// </summary>
    private sealed class GatedRegistry(string refusedValueName) : IRegistryService
    {
        private readonly UiFakeRegistryService _inner = new();
        public string RefusedValueName { get; } = refusedValueName;
        public int ScanCount { get; private set; }
        public List<string> Writes { get; } = [];

        public void SetDWord(string keyPath, string valueName, int value) => _inner.SetDWord(keyPath, valueName, value);

        public OperationResult<int> ReadDWord(string keyPath, string valueName)
        {
            if (valueName == "RotatingLockScreenOverlayEnabled")
                ScanCount++;
            return _inner.ReadDWord(keyPath, valueName);
        }

        public OperationResult<bool> WriteDWord(string keyPath, string valueName, int value)
        {
            Writes.Add(valueName);
            return valueName == RefusedValueName
                ? OperationResult<bool>.Failure("Registry access denied", ErrorCategory.AccessDenied)
                : _inner.WriteDWord(keyPath, valueName, value);
        }

        public OperationResult<string> ReadString(string keyPath, string valueName) => _inner.ReadString(keyPath, valueName);
        public OperationResult<string> ReadExpandString(string keyPath, string valueName) => _inner.ReadExpandString(keyPath, valueName);
        public OperationResult<string[]> ReadMultiString(string keyPath, string valueName) => _inner.ReadMultiString(keyPath, valueName);
        public OperationResult<byte[]> ReadBinary(string keyPath, string valueName) => _inner.ReadBinary(keyPath, valueName);
        public OperationResult<bool> WriteString(string keyPath, string valueName, string value) => _inner.WriteString(keyPath, valueName, value);
        public OperationResult<bool> WriteExpandString(string keyPath, string valueName, string value) => _inner.WriteExpandString(keyPath, valueName, value);
        public OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values) => _inner.WriteMultiString(keyPath, valueName, values);
        public OperationResult<bool> WriteBinary(string keyPath, string valueName, byte[] value) => _inner.WriteBinary(keyPath, valueName, value);
        public OperationResult<bool> DeleteValue(string keyPath, string valueName) => _inner.DeleteValue(keyPath, valueName);
        public OperationResult<bool> DeleteKey(string keyPath, bool recursive = false) => _inner.DeleteKey(keyPath, recursive);
        public OperationResult<bool> KeyExists(string keyPath) => _inner.KeyExists(keyPath);
        public OperationResult<bool> ValueExists(string keyPath, string valueName) => _inner.ValueExists(keyPath, valueName);
        public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath) => _inner.EnumerateSubKeys(keyPath);
        public OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath) => _inner.EnumerateValues(keyPath);
        public OperationResult<string> ReadValueBeforeWrite(string keyPath, string valueName) => _inner.ReadValueBeforeWrite(keyPath, valueName);
    }

    private sealed class RecordingHistory : IChangeHistoryService
    {
        public List<MutationResult> Recorded { get; } = [];
        public Task InitializeAsync() => Task.CompletedTask;
        public Task RecordChangesAsync(MutationResult result) { Recorded.Add(result); return Task.CompletedTask; }
        public Task RecordDriftEventsAsync(IReadOnlyList<ChangeHistoryEntry> driftEntries) => Task.CompletedTask;
        public Task<IReadOnlyList<ChangeHistoryEntry>> GetHistoryAsync(int? limit = null, int? offset = null) => Task.FromResult<IReadOnlyList<ChangeHistoryEntry>>([]);
        public Task<IReadOnlyList<ChangeHistoryEntry>> GetRecentGroupedAsync(int groupLimit = 50) => Task.FromResult<IReadOnlyList<ChangeHistoryEntry>>([]);
        public Task<int> GetGroupCountAsync() => Task.FromResult(0);
        public Task<OperationResult<bool>> RevertChangeAsync(long historyId, Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc) => Task.FromResult(OperationResult<bool>.Success(true));
        public Task<OperationResult<bool>> RedoChangeAsync(long historyId, Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc) => Task.FromResult(OperationResult<bool>.Success(true));
        public Task<int> GetEntryCountAsync() => Task.FromResult(0);
        public Task ClearHistoryAsync() => Task.CompletedTask;
    }

    private sealed class NoExplorer : IExplorerRestartService
    {
        public Task<OperationResult<bool>> RestartExplorerAsync() => Task.FromResult(OperationResult<bool>.Success(true));
        public Task<OperationResult<bool>> RefreshExplorerViewsAsync() => Task.FromResult(OperationResult<bool>.Success(true));
    }

    private sealed class NoSets : ISetProvider
    {
        public SetLoadResult LoadSets() => new() { Sets = [], Warnings = [] };
    }

    /// <summary>A DWORD change the real module applies by writing this value name under the CDM key.</summary>
    private static ChangeDescriptor Change(string valueName, string displayName) => new()
    {
        ModuleId = ModuleId,
        SettingId = valueName,
        DisplayName = displayName,
        SystemLocation = $@"{AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath}\{valueName}",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Off",
        AfterDisplay = "On",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Enable,
    };

    private static ChangeGroup Group(string id, ChangeDescriptor change) => new()
    {
        GroupId = id,
        DisplayName = change.DisplayName,
        Description = change.DisplayName,
        Changes = [change],
    };

    private sealed record Harness(MainWindowViewModel Vm, PendingChangesService Pending, RecordingHistory History, GatedRegistry Registry);

    private static async Task<Harness> CreateAsync(string refusedValueName)
    {
        var registry = new GatedRegistry(refusedValueName);
        var module = new AnnoyancesModule(registry);
        var pending = new PendingChangesService();
        var history = new RecordingHistory();
        var tempDir = Path.Combine(Path.GetTempPath(), $"tipc-rescan-{Guid.NewGuid():N}");
        var vm = new MainWindowViewModel(
            new NavigationService([module]), pending, history, registry, new NoExplorer(),
            new ReviewPanelViewModel(pending, new CustomSetWriter(tempDir)), new NoSets(), [],
            new CustomSetWriter(tempDir), new UiFakeRestorePointService());
        await vm.InitializeAsync();
        return new Harness(vm, pending, history, registry);
    }

    private static SettingCardViewModel LockScreenCard(MainWindowViewModel vm) =>
        Assert.IsType<AnnoyancesViewModel>(vm.CurrentContent).CardGroups
            .SelectMany(g => g.Cards)
            .Single(c => c.Model.SettingId == "lock-screen-ads");

    /// <summary>Flip the two lock-screen values to their suppressed state behind the page's back.</summary>
    private static void SuppressLockScreenAdsInRegistry(GatedRegistry registry)
    {
        registry.SetDWord(AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "RotatingLockScreenOverlayEnabled", 0);
        registry.SetDWord(AnnoyancesRegistryPaths.ContentDeliveryManagerKeyPath, "SubscribedContent-338387Enabled", 0);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.ElapsedMilliseconds < 5000, "Timed out waiting for the module page to load.");
            await Task.Delay(10);
        }
    }

    [AvaloniaFact]
    public async Task DiscardAll_AfterUnresolvedGroup_ReadsTheModuleAgainBeforeItCanRestage()
    {
        var (vm, pending, history, registry) = await CreateAsync(refusedValueName: "Refused");
        vm.NavigateToModuleCommand.Execute(vm.SidebarGroups.SelectMany(g => g.Items).Single());
        await WaitUntilAsync(() => vm.CurrentContent is AnnoyancesViewModel);
        var scansAtLoad = registry.ScanCount;
        var pageAtLoad = vm.CurrentContent;
        Assert.Equal("0", LockScreenCard(vm).Model.CurrentValue);

        pending.Stage(Group("g2", Change("Refused", "Second setting")));
        await vm.ApplyAllCommand.ExecuteAsync(null);
        Assert.True(vm.HasUnresolvedGroups);
        Assert.Empty(history.Recorded);

        // The machine moved on while the group sat unresolved; the loaded cards
        // still hold the values from the earlier scan.
        SuppressLockScreenAdsInRegistry(registry);
        Assert.Equal("0", LockScreenCard(vm).Model.CurrentValue);

        await ((IAsyncRelayCommand)vm.DiscardAllCommand).ExecuteAsync(null);

        // Queue cleared, block lifted, page re-read from the module so the cards
        // stage from what Windows has now, never from the earlier scan.
        Assert.Equal(0, pending.PendingCount);
        Assert.Empty(pending.ReconciliationRequired);
        Assert.False(vm.HasUnresolvedGroups);
        Assert.False(vm.ReviewPanel.HasUnresolvedGroups);
        Assert.Equal(scansAtLoad + 1, registry.ScanCount);
        Assert.NotSame(pageAtLoad, vm.CurrentContent);
        Assert.Equal("1", LockScreenCard(vm).Model.CurrentValue);
        Assert.False(vm.IsReviewPanelOpen);
        Assert.Contains("reloaded", vm.StatusMessage, StringComparison.Ordinal);

        // Discard undid nothing on the PC: no second write ran for the uncertain change.
        Assert.Equal(["Refused"], registry.Writes);

        // A fresh group applies normally afterwards and is recorded.
        pending.Stage(Group("g4", Change("Allowed", "First setting")));
        await vm.ApplyAllCommand.ExecuteAsync(null);
        Assert.Equal(0, pending.PendingCount);
        Assert.Equal(["Refused", "Allowed"], registry.Writes);
        var recorded = Assert.Single(history.Recorded);
        Assert.True(recorded.IsSuccess);
        Assert.Equal("Changes applied successfully", vm.StatusMessage);
    }

    [AvaloniaFact]
    public async Task DiscardAll_WithNothingUnresolved_DoesNotRescan()
    {
        var (vm, pending, _, registry) = await CreateAsync(refusedValueName: "none");
        vm.NavigateToModuleCommand.Execute(vm.SidebarGroups.SelectMany(g => g.Items).Single());
        await WaitUntilAsync(() => vm.CurrentContent is AnnoyancesViewModel);
        var scansAtLoad = registry.ScanCount;
        var pageAtLoad = vm.CurrentContent;
        pending.Stage(Group("g1", Change("Allowed", "First setting")));

        await ((IAsyncRelayCommand)vm.DiscardAllCommand).ExecuteAsync(null);

        Assert.Equal(0, pending.PendingCount);
        Assert.Equal(scansAtLoad, registry.ScanCount);
        Assert.Same(pageAtLoad, vm.CurrentContent);
    }

    [AvaloniaFact]
    public async Task DiscardAll_WhileOnAnotherPage_DoesNotRescanTheUnresolvedModule()
    {
        var (vm, pending, _, registry) = await CreateAsync(refusedValueName: "Refused");
        // Home is open: the module has no page on screen, so nothing is stale.
        var scansAtStart = registry.ScanCount;
        pending.Stage(Group("g2", Change("Refused", "Second setting")));
        await vm.ApplyAllCommand.ExecuteAsync(null);
        Assert.True(vm.HasUnresolvedGroups);

        await ((IAsyncRelayCommand)vm.DiscardAllCommand).ExecuteAsync(null);

        Assert.False(vm.HasUnresolvedGroups);
        Assert.Equal(scansAtStart, registry.ScanCount);
        Assert.IsType<HomeViewModel>(vm.CurrentContent);

        // Opening the page later scans it fresh, as every navigation does.
        vm.NavigateToModuleCommand.Execute(vm.SidebarGroups.SelectMany(g => g.Items).Single());
        await WaitUntilAsync(() => vm.CurrentContent is AnnoyancesViewModel);
        Assert.Equal(scansAtStart + 1, registry.ScanCount);
    }
}
