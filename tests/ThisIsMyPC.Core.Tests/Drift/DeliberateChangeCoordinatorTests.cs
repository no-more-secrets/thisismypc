using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Baseline;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Drift;

public sealed class DeliberateChangeCoordinatorTests
{
    [Fact]
    public async Task ApplyHoldsOneLeaseThroughWriteHistoryAndChosenValue()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync();
        f.Registry.Value = f.Target.SuppressedValue;
        var coordinator = Create(f);
        var pending = new PendingChangesService(); pending.Stage(Change(f, "0", "1"));
        var history = History(f, coordinator);
        var requests = f.Provider.Requests.Count;
        f.Registry.BeforeWrite = () => Assert.Empty(f.Baseline.Read(f.Provider.CurrentLease!));
        var result = await coordinator.ApplyAsync(pending, history, c => Write(f, c), c => Write(f, c));
        Assert.True(result.IsSuccess);
        Assert.Equal(requests + 1, f.Provider.Requests.Count);
        Assert.Single(await history.GetHistoryAsync());
        Assert.Null(f.Provider.CurrentLease);
        Assert.Equal(RestorationScanStatus.AlreadyMatches, (await f.Loop.ScanAsync()).Status);
    }

    [Fact]
    public async Task StagedValueChangedWhileWaitingRefusesBeforeAnyWrite()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync();
        var coordinator = Create(f, onRecovery: () => f.Registry.Value = f.Target.SuppressedValue);
        var pending = new PendingChangesService(); pending.Stage(Change(f, "1", "0"));
        var bytes = f.Storage.Bytes!.ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ApplyAsync(pending, History(f), c => Write(f, c), c => Write(f, c)));
        Assert.Equal(0, f.Registry.Writes); Assert.Equal(bytes, f.Storage.Bytes); Assert.False(pending.IsApplying);
        Assert.Equal(1, pending.PendingCount);
    }

    [Fact]
    public async Task QueueChangedDuringAcquisitionPreparesTheActualSnapshot()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync();
        f.Registry.Value = f.Target.SuppressedValue;
        var pending = new PendingChangesService();
        var coordinator = Create(f, onRecovery: () => pending.Stage(Change(f, "0", "1")));
        f.Registry.BeforeWrite = () => Assert.Empty(f.Baseline.Read(f.Provider.CurrentLease!));
        Assert.True((await coordinator.ApplyAsync(pending, History(f), c => Write(f, c), c => Write(f, c))).IsSuccess);
        Assert.Equal(1, f.Registry.Writes);
    }

    [Fact]
    public async Task HistoryFailureAfterWriteLeavesNoStaleChoiceOnRestart()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync();
        f.Registry.Value = f.Target.SuppressedValue; f.Registry.BeforeWrite = null;
        await Sql(f, "CREATE TRIGGER refuse_history BEFORE INSERT ON change_history BEGIN SELECT RAISE(ABORT, 'test'); END;");
        var pending = new PendingChangesService(); pending.Stage(Change(f, "0", "1"));
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => Create(f).ApplyAsync(pending, History(f), c => Write(f, c), c => Write(f, c)));
        await f.InitializeAsync(false);
        Assert.Equal(RestorationScanStatus.BaselineMissing, (await f.Loop.ScanAsync()).Status);
        Assert.Equal("1", f.Registry.Value!.Data); Assert.Equal(1, f.Registry.Writes);
    }

    [Fact]
    public async Task BaselinePersistenceFailureAfterHistoryCannotRestoreOldChoice()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync();
        f.Registry.Value = f.Target.SuppressedValue; f.Registry.BeforeWrite = null;
        var storage = new FailingStorage(f.Storage) { FailAt = 2 };
        var coordinator = Create(f, new(storage, f.Provider.Name, RestorationLoopFixture.Sid));
        var pending = new PendingChangesService(); pending.Stage(Change(f, "0", "1"));
        await Assert.ThrowsAsync<IOException>(() => coordinator.ApplyAsync(pending, History(f), c => Write(f, c), c => Write(f, c)));
        Assert.Single(await f.Repository.GetAllAsync());
        await f.InitializeAsync(false);
        Assert.Equal(RestorationScanStatus.BaselineMissing, (await f.Loop.ScanAsync()).Status);
        Assert.Equal("1", f.Registry.Value!.Data);
    }

    [Fact]
    public async Task InhibitionFailurePreventsTheFirstWrite()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync();
        var storage = new FailingStorage(f.Storage) { FailAt = 1 };
        var coordinator = Create(f, new(storage, f.Provider.Name, RestorationLoopFixture.Sid));
        var pending = new PendingChangesService(); pending.Stage(Change(f, "1", "0"));
        await Assert.ThrowsAsync<IOException>(() => coordinator.ApplyAsync(pending, History(f), c => Write(f, c), c => Write(f, c)));
        Assert.Equal(0, f.Registry.Writes); Assert.Empty(await f.Repository.GetAllAsync());
    }

    [Fact]
    public async Task ServiceCannotScanDuringDeliberateWrite()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync();
        var pending = new PendingChangesService(); pending.Stage(Change(f, "1", "0"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Registry.BeforeWrite = null;
        var app = Create(f).ApplyAsync(pending, History(f), async c =>
        {
            entered.SetResult(); await release.Task; return await Write(f, c);
        }, c => Write(f, c));
        await entered.Task;
        Assert.Equal(RestorationScanStatus.CoordinationBlocked, (await f.Loop.ScanAsync()).Status);
        release.SetResult(); Assert.True((await app).IsSuccess);
    }

    [Fact]
    public async Task UndoAndRedoEachAcquireOnceAndReplaceProtectedChoice()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync(); f.Registry.BeforeWrite = null;
        var coordinator = Create(f); var history = History(f, coordinator);
        var pending = new PendingChangesService(); pending.Stage(Change(f, "1", "0"));
        await coordinator.ApplyAsync(pending, history, c => Write(f, c), c => Write(f, c));
        var original = Assert.Single(await history.GetHistoryAsync());
        var count = f.Provider.Requests.Count;
        Assert.True((await history.RevertChangeAsync(original.Id, c => Write(f, c))).IsSuccess);
        Assert.Equal(count + 1, f.Provider.Requests.Count);
        Assert.Equal(RestorationScanStatus.AlreadyMatches, (await f.Loop.ScanAsync()).Status);
        count = f.Provider.Requests.Count;
        Assert.True((await history.RedoChangeAsync(original.Id, c => Write(f, c))).IsSuccess);
        Assert.Equal(count + 1, f.Provider.Requests.Count);
        Assert.Equal(RestorationScanStatus.AlreadyMatches, (await f.Loop.ScanAsync()).Status);
    }

    [Fact]
    public async Task UndoOfOriginallyAbsentValueDeletesAndRemovesProtection()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync(); f.Registry.Value = null; f.Registry.BeforeWrite = null;
        var coordinator = Create(f); var history = History(f, coordinator);
        var pending = new PendingChangesService(); pending.Stage(Change(f, "", "0"));
        await coordinator.ApplyAsync(pending, history, c => Write(f, c), c => Write(f, c));
        var row = Assert.Single(await history.GetHistoryAsync());
        Assert.True((await history.RevertChangeAsync(row.Id, c => Write(f, c))).IsSuccess);
        Assert.Null(f.Registry.Value);
        Assert.Equal(RestorationScanStatus.BaselineMissing, (await f.Loop.ScanAsync()).Status);
        Assert.True((await history.RedoChangeAsync(row.Id, c => Write(f, c))).IsSuccess);
        Assert.Equal(RestorationScanStatus.AlreadyMatches, (await f.Loop.ScanAsync()).Status);
    }

    [Fact]
    public async Task UncertainWriteLeavesRemovedChoiceEvenIfValueLooksUnchanged()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync(); f.Registry.BeforeWrite = null; f.Registry.FailWrite = true;
        var pending = new PendingChangesService(); pending.Stage(Change(f, "1", "0"));
        var result = await Create(f).ApplyAsync(pending, History(f), c => Write(f, c), c => Write(f, c));
        Assert.True(result.HasUncertainState);
        Assert.Equal(RestorationScanStatus.BaselineMissing, (await f.Loop.ScanAsync()).Status);
    }

    [Theory]
    [InlineData(ErrorCategory.AccessDenied)]
    [InlineData(ErrorCategory.ServiceUnavailable)]
    public async Task FailedTypedReadNeverBecomesAnAbsentBeforeValue(ErrorCategory category)
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync(); f.Registry.ReadError = category;
        var pending = new PendingChangesService(); pending.Stage(Change(f, "", "0"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(f).ApplyAsync(pending, History(f), c => Write(f, c), c => Write(f, c)));
        Assert.Equal(0, f.Registry.Writes);
    }

    [Fact]
    public async Task CancelledWaitNeverPreparesOrWrites()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        var provider = new WaitingProvider(f.Provider.Name);
        var coordinator = new DeliberateChangeCoordinator(new(provider, (_, _) => throw new InvalidOperationException("Recovery must not run.")),
            f.Baseline, f.Registry, TimeProvider.System);
        var pending = new PendingChangesService(); pending.Stage(Change(f, "1", "0"));
        var before = f.Storage.Bytes!.ToArray();
        var applying = coordinator.ApplyAsync(pending, History(f), c => Write(f, c), c => Write(f, c), cancellationToken: cancellation.Token);
        await provider.Entered.Task; cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => applying);
        Assert.Equal(before, f.Storage.Bytes); Assert.Equal(0, f.Registry.Writes); Assert.Equal(1, pending.PendingCount);
    }

    [Fact]
    public async Task RollbackUsesTheSameLeaseAndNeverReinstatesAnUncertainChoice()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync(); f.Registry.BeforeWrite = null;
        var pending = new PendingChangesService();
        pending.Stage(new ChangeGroup { GroupId = "test", DisplayName = "test group", Description = "test",
            Changes = [Change(f, "1", "0"), Change(f, "1", "0") with
            {
                SettingId = "noncatalog", SystemLocation = @"HKLM\Software\Test\Other",
            }] });
        var acquisitions = f.Provider.Requests.Count;
        var result = await Create(f).ApplyAsync(pending, History(f), c => c.SettingId == "noncatalog"
            ? Task.FromResult(OperationResult<bool>.Failure("test", ErrorCategory.AccessDenied)) : Write(f, c), c => Write(f, c));
        Assert.False(result.IsSuccess); Assert.Single(result.RolledBack); Assert.Equal("1", f.Registry.Value!.Data);
        Assert.Equal(acquisitions + 1, f.Provider.Requests.Count); Assert.Empty(await f.Repository.GetAllAsync());
        Assert.Equal(RestorationScanStatus.BaselineMissing, (await f.Loop.ScanAsync()).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AmbiguousSaveIsDisarmedBeforeRollback(bool removalFails)
    {
        using var f = new RestorationLoopFixture();
        await f.InitializeAsync();
        f.Registry.BeforeWrite = null;
        var storage = new AmbiguousStorage(f.Storage);
        var coordinator = Create(f, new(storage, f.Provider.Name, RestorationLoopFixture.Sid));
        await coordinator.RunAsync(async (session, token) =>
        {
            var change = Change(f, "1", "0");
            session.Prepare([change]);
            Assert.True((await Write(f, change)).IsSuccess);
            storage.ThrowAfterNextWrite = true;
            Assert.Throws<IOException>(() => session.RecordApplied([change]));
            storage.FailWrites = removalFails;
            session.DisableProtection(f.Consent);
            Assert.True((await Write(f, Change(f, "0", "1"))).IsSuccess);
            return true;
        });
        if (removalFails) Assert.False(f.Consent.Enabled);
        else Assert.Equal(RestorationScanStatus.BaselineMissing, (await f.Loop.ScanAsync()).Status);
        Assert.Equal("1", f.Registry.Value!.Data);
    }

    private sealed class AmbiguousStorage(ITrustedBaselineStorage inner) : ITrustedBaselineStorage
    {
        public bool ThrowAfterNextWrite { get; set; }
        public bool FailWrites { get; set; }
        public byte[]? Read(int maximumBytes) => inner.Read(maximumBytes);
        public void ReplaceDurably(ReadOnlyMemory<byte> document)
        {
            if (FailWrites) throw new IOException("Storage unavailable.");
            inner.ReplaceDurably(document);
            if (!ThrowAfterNextWrite) return;
            ThrowAfterNextWrite = false;
            throw new IOException("Committed, then failed acknowledgement.");
        }
    }

    private sealed class WaitingProvider(string name) : IMutationLeaseProvider
    {
        public string Name => name;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MutationLeaseResult> AcquireAsync(TimeSpan maxWait, CancellationToken cancellationToken = default)
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() => cancelled.TrySetResult());
            Entered.SetResult(); await cancelled.Task; return MutationLeaseResult.Cancelled();
        }
    }

    private static DeliberateChangeCoordinator Create(RestorationLoopFixture f, SingleOwnerBaselineStore? baseline = null, Action? onRecovery = null)
        => new(new(f.Provider, (_, _) => { onRecovery?.Invoke(); return Task.FromResult(OperationResult<bool>.Success(true)); }),
            baseline ?? f.Baseline, f.Registry, TimeProvider.System);
    private static ChangeHistoryService History(RestorationLoopFixture f, DeliberateChangeCoordinator? coordinator = null)
        => new(f.Repository, Path.Combine(f.DirectoryPath, "history.db"), deliberateChanges: coordinator);
    private static ChangeDescriptor Change(RestorationLoopFixture f, string before, string after) => new()
    {
        ModuleId = f.Target.ModuleId, SettingId = f.Target.SettingId, DisplayName = f.Target.DisplayName,
        SystemLocation = f.Target.KeyPath + "\\" + f.Target.ValueName, ValueType = ChangeValueType.Registry_DWord,
        BeforeValue = before, AfterValue = after, BeforeDisplay = before, AfterDisplay = after,
    };
    private static Task<OperationResult<bool>> Write(RestorationLoopFixture f, ChangeDescriptor change)
    {
        Assert.True(f.Provider.CurrentLease!.CanWrite);
        if (change.AfterValue == "") { f.Registry.Value = null; return Task.FromResult(OperationResult<bool>.Success(true)); }
        return Task.FromResult(f.Registry.WriteValue(f.Registry.ExpectedPath, f.Registry.ExpectedName,
            new(RegistryValueDataKind.DWord, change.AfterValue!)));
    }
    private static async Task Sql(RestorationLoopFixture f, string sql)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(f.DirectoryPath, "history.db")}");
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
    private sealed class FailingStorage(ITrustedBaselineStorage inner) : ITrustedBaselineStorage
    {
        public int FailAt { get; init; }
        private int _writes;
        public byte[]? Read(int maximumBytes) => inner.Read(maximumBytes);
        public void ReplaceDurably(ReadOnlyMemory<byte> document)
        {
            if (++_writes == FailAt) throw new IOException("Simulated storage failure.");
            inner.ReplaceDurably(document);
        }
    }
}
