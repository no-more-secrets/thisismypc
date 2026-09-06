using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Data;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Baseline;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class OwnerMutationCommandFailureTests
{
    private static DeliberateChangeCoordinator RefusingCoordinator(IMutationLeaseProvider? supplied = null)
    {
        var provider = supplied ?? new RefusingProvider();
        return new(new MutationCoordinator(provider, (_, _) => throw new InvalidOperationException("Recovery must not run")),
            new SingleOwnerBaselineStore(new UnusedStorage(), provider.Name, "S-1-5-21-111-222-333-1001"),
            new Fakes.FakeRegistryService(), TimeProvider.System);
    }

    [Fact]
    public async Task ApplyLeaseRefusalShowsErrorAndRetainsQueue()
    {
        var pending = new PendingChangesService();
        var history = new Fakes.FakeChangeHistoryService();
        var writer = new CustomSetWriter(Path.Combine(Path.GetTempPath(), "tipc-command-test"));
        var vm = new MainWindowViewModel(new NavigationService([]), pending, history,
            new Fakes.FakeRegistryService(), new Fakes.FakeExplorerRestartService(),
            new ReviewPanelViewModel(pending, writer), new Fakes.FakeSetProvider(), [], writer,
            new Fakes.FakeRestorePointService(), deliberateChanges: RefusingCoordinator());
        pending.Stage(new ChangeDescriptor
        {
            ModuleId = "test", SettingId = "test", DisplayName = "test", SystemLocation = @"HKCU\test\value",
            BeforeValue = "0", AfterValue = "1", BeforeDisplay = "Off", AfterDisplay = "On",
            ValueType = ChangeValueType.Registry_DWord, Category = ChangeCategory.Modify,
        });
        await vm.ApplyAllCommand.ExecuteAsync(null);
        Assert.Contains("lease", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsApplying);
        Assert.Equal(1, pending.PendingCount);
        Assert.Empty(history.RecordedResults);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryLeaseRefusalShowsError(bool redo)
    {
        var history = new ChangeHistoryService(new ChangeHistoryRepository(), deliberateChanges: RefusingCoordinator());
        var vm = new ChangeHistoryViewModel(history, _ => throw new InvalidOperationException("No writer expected"),
            _ => throw new InvalidOperationException("No writer expected"),
            new CustomSetWriter(Path.Combine(Path.GetTempPath(), "tipc-command-test")));
        var entry = new ChangeHistoryEntryViewModel
        {
            Id = 1, DisplayName = "test", ModuleId = "test", SystemLocation = @"HKCU\test\value",
            BeforeDisplay = "Off", AfterDisplay = "On", Category = ChangeCategory.Modify,
            AppliedAt = DateTimeOffset.UtcNow, IsReverted = redo,
        };
        if (redo) await vm.RedoCommand.ExecuteAsync(entry);
        else await vm.RestoreCommand.ExecuteAsync(entry);
        Assert.Contains("lease", vm.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShutdownCancelsAnAcquisitionWait()
    {
        var provider = new WaitingProvider();
        using var coordinator = RefusingCoordinator(provider);
        var pending = new PendingChangesService();
        var history = new Fakes.FakeChangeHistoryService();
        var vm = Window(pending, history, coordinator);
        Stage(pending);
        var apply = vm.ApplyAllCommand.ExecuteAsync(null);
        await provider.Entered.Task;
        await vm.PrepareForShutdownAsync();
        Assert.True(apply.IsCompleted);
        Assert.False(vm.IsApplying);
        Assert.Empty(history.RecordedResults);
        Assert.Equal(1, pending.PendingCount);
    }

    [Fact]
    public async Task ShutdownWaitsForAnActiveWriteAndItsHistory()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var module = new Fakes.FakeModule("test", async _ =>
        {
            entered.SetResult();
            await release.Task;
            return OperationResult<bool>.Success(true);
        });
        var pending = new PendingChangesService();
        var history = new Fakes.FakeChangeHistoryService();
        var vm = Window(pending, history, null, module);
        await vm.InitializeAsync();
        Stage(pending);
        var apply = vm.ApplyAllCommand.ExecuteAsync(null);
        await entered.Task;
        var shutdown = vm.PrepareForShutdownAsync();
        Assert.False(shutdown.IsCompleted);
        release.SetResult();
        await shutdown;
        Assert.True(apply.IsCompleted);
        Assert.Single(history.RecordedResults);
        Assert.False(vm.IsApplying);
    }

    private static MainWindowViewModel Window(PendingChangesService pending, Fakes.FakeChangeHistoryService history,
        DeliberateChangeCoordinator? coordinator, params Core.Modules.IModule[] modules)
    {
        var writer = new CustomSetWriter(Path.Combine(Path.GetTempPath(), "tipc-command-test"));
        return new(new NavigationService(modules), pending, history, new Fakes.FakeRegistryService(),
            new Fakes.FakeExplorerRestartService(), new ReviewPanelViewModel(pending, writer),
            new Fakes.FakeSetProvider(), [], writer, new Fakes.FakeRestorePointService(), deliberateChanges: coordinator);
    }

    private static void Stage(PendingChangesService pending) => pending.Stage(new ChangeDescriptor
    {
        ModuleId = "test", SettingId = "test", DisplayName = "test", SystemLocation = @"HKCU\test\value",
        BeforeValue = "0", AfterValue = "1", BeforeDisplay = "Off", AfterDisplay = "On",
        ValueType = ChangeValueType.Registry_DWord, Category = ChangeCategory.Modify,
    });

    private sealed class WaitingProvider : IMutationLeaseProvider
    {
        public string Name { get; } = MutationLeaseNames.ForTest();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MutationLeaseResult> AcquireAsync(TimeSpan maxWait, CancellationToken cancellationToken = default)
        {
            Entered.SetResult();
            var gate = new TaskCompletionSource<MutationLeaseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            try { return await gate.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { return MutationLeaseResult.Cancelled(); }
        }
    }
    private sealed class RefusingProvider : IMutationLeaseProvider
    {
        public string Name { get; } = MutationLeaseNames.ForTest();
        public async Task<MutationLeaseResult> AcquireAsync(TimeSpan maxWait, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return MutationLeaseResult.TimedOut(maxWait);
        }
    }
    private sealed class UnusedStorage : ITrustedBaselineStorage
    {
        public byte[]? Read(int maximumBytes) => throw new InvalidOperationException("No read expected");
        public void ReplaceDurably(ReadOnlyMemory<byte> document) => throw new InvalidOperationException("No write expected");
    }
}
