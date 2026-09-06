using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Data;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Baseline;
using ThisIsMyPC.Core.Drift.Consent;
using ThisIsMyPC.Core.Drift.Eligibility;
using ThisIsMyPC.Core.Drift.Journal;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Tests.Coordination;

namespace ThisIsMyPC.Core.Tests.Drift;

internal sealed class RestorationLoopFixture : IDisposable
{
    public const string Sid = "S-1-5-21-111-222-333-1001";
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "tipc-loop", Guid.NewGuid().ToString("N"));
    public FakeMutationLeaseProvider Provider { get; } = new();
    public MemoryStorage Storage { get; } = new();
    public ConsentStore Consent { get; } = new();
    public LoopRegistry Registry { get; } = new();
    public RestorationTarget Target { get; } = RestorationCatalog.Default.Targets[0];
    public SingleOwnerBaselineStore Baseline { get; }
    public RestorationJournal Journal { get; }
    public ChangeHistoryRepository Repository { get; } = new();
    public RestorationLoop Loop { get; private set; } = null!;
    public RestorationProfileState Profile { get; set; } = RestorationProfileState.SupportedAndLoaded;
    public RestorationManagementState Management { get; set; } = RestorationManagementState.Unmanaged;
    public Action<IMutationLease>? Recovering { get; set; }
    public RestorationLoopFixture(TimeProvider? time = null)
    {
        Directory.CreateDirectory(Path.Combine(DirectoryPath, "journal"));
        Baseline = new(Storage, Provider.Name, Sid);
        Journal = new(Path.Combine(DirectoryPath, "journal"), new Guard(), _ => true, timeProvider: time);
        Registry.Value = Target.WindowsDefaultValue;
        Registry.ExpectedPath = "HKU\\" + Sid + "\\" + Target.KeyPath[5..];
        Registry.ExpectedName = Target.ValueName;
        Registry.BeforeWrite = () =>
        {
            var lease = Provider.CurrentLease!;
            Assert.True(lease.CanWrite);
            Assert.Contains(Journal.Read(lease).Attempts, a => a.Outcome is null);
        };
    }
    public async Task InitializeAsync(bool saveBaseline = true)
    {
        await Repository.InitializeDatabaseAsync(Path.Combine(DirectoryPath, "history.db"));
        var coordinator = new MutationCoordinator(Provider, (lease, _) =>
        {
            Recovering?.Invoke(lease);
            return Task.FromResult(OperationResult<bool>.Success(true));
        });
        if (saveBaseline)
            await coordinator.RunAsync(TimeSpan.Zero, (lease, _) =>
            {
                Baseline.RecordApplied([new(Target.ModuleId, Target.SettingId, Target.KeyPath + "\\" + Target.ValueName,
                    Target.SuppressedValue, Journal.TimeProvider.GetUtcNow())], lease);
                return Task.FromResult(true);
            });
        Loop = new(coordinator, Baseline, Target, Consent, Journal, new(Repository), Registry,
            identity => new(new(Sid, Profile), new(identity, Management)), Journal.TimeProvider);
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(DirectoryPath, true);
    }
    internal sealed class MemoryStorage : ITrustedBaselineStorage
    {
        public byte[]? Bytes { get; set; }
        public byte[]? Read(int maximumBytes) => Bytes;
        public void ReplaceDurably(ReadOnlyMemory<byte> document) => Bytes = document.ToArray();
    }
    internal sealed class Guard : IDataDirectoryGuard
    {
        public OperationResult<DaclStatus> EnsureHardened(string path) => OperationResult<DaclStatus>.Success(DaclStatus.Verified);
    }
    internal sealed class ConsentStore : IMachineConsentStore
    {
        public bool Enabled { get; set; } = true;
        public MachineConsentState Read() => new(MachineConsentStatus.Loaded, Enabled, "test");
        public MachineConsentWriteResult SetEnabled(bool enabled, IMutationLease lease) { Enabled = enabled; return new(true, Read()); }
    }
    internal sealed class LoopRegistry : IRegistryService
    {
        public RegistryValueData? Value { get; set; }
        public string ExpectedPath { get; set; } = "";
        public string ExpectedName { get; set; } = "";
        public int Writes { get; private set; }
        public Action? BeforeWrite { get; set; }
        public ErrorCategory? ReadError { get; set; }
        public bool FailWrite { get; set; }
        public bool IgnoreWrite { get; set; }
        public OperationResult<RegistryValueData> ReadValue(string keyPath, string valueName)
        {
            Assert.Equal(ExpectedPath, keyPath); Assert.Equal(ExpectedName, valueName);
            return ReadError is { } error ? OperationResult<RegistryValueData>.Failure("read failed", error) :
                Value is { } value ? OperationResult<RegistryValueData>.Success(value) :
                OperationResult<RegistryValueData>.Failure("absent", ErrorCategory.NotFound);
        }
        public OperationResult<bool> WriteValue(string keyPath, string valueName, RegistryValueData value)
        {
            Assert.Equal(ExpectedPath, keyPath); Assert.Equal(ExpectedName, valueName);
            BeforeWrite?.Invoke(); Writes++;
            if (FailWrite) return OperationResult<bool>.Failure("write failed", ErrorCategory.AccessDenied);
            if (!IgnoreWrite) Value = value;
            return OperationResult<bool>.Success(true);
        }
        public OperationResult<int> ReadDWord(string p, string n) => throw new NotSupportedException();
        public OperationResult<string> ReadString(string p, string n) => throw new NotSupportedException();
        public OperationResult<string> ReadExpandString(string p, string n) => throw new NotSupportedException();
        public OperationResult<string[]> ReadMultiString(string p, string n) => throw new NotSupportedException();
        public OperationResult<byte[]> ReadBinary(string p, string n) => throw new NotSupportedException();
        public OperationResult<bool> WriteDWord(string p, string n, int v) => throw new NotSupportedException();
        public OperationResult<bool> WriteString(string p, string n, string v) => throw new NotSupportedException();
        public OperationResult<bool> WriteExpandString(string p, string n, string v) => throw new NotSupportedException();
        public OperationResult<bool> WriteMultiString(string p, string n, string[] v) => throw new NotSupportedException();
        public OperationResult<bool> WriteBinary(string p, string n, byte[] v) => throw new NotSupportedException();
        public OperationResult<bool> DeleteValue(string p, string n) => throw new NotSupportedException();
        public OperationResult<bool> DeleteKey(string p, bool recursive = false) => throw new NotSupportedException();
        public OperationResult<bool> KeyExists(string p) => throw new NotSupportedException();
        public OperationResult<bool> ValueExists(string p, string n) => throw new NotSupportedException();
        public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string p) => throw new NotSupportedException();
        public OperationResult<IReadOnlyList<string>> EnumerateValues(string p) => throw new NotSupportedException();
        public OperationResult<string> ReadValueBeforeWrite(string p, string n) => throw new NotSupportedException();
    }
}

public sealed class RestorationLoopTests
{
    [Fact]
    public async Task RestoresOnceThroughDurableIntentAndHistoryThenMatches()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync();
        Assert.Equal(RestorationScanStatus.Restored, (await f.Loop.ScanAsync()).Status);
        Assert.Equal(RestorationScanStatus.AlreadyMatches, (await f.Loop.ScanAsync()).Status);
        Assert.Equal(1, f.Registry.Writes);
        var row = Assert.Single(await f.Repository.GetAllAsync());
        Assert.Equal("Applied", row.JournalOutcome); Assert.False(row.SupportsGenericUndo);
    }
    [Theory]
    [InlineData(RestorationProfileState.Unknown)]
    [InlineData(RestorationProfileState.Unsupported)]
    [InlineData(RestorationProfileState.Unloaded)]
    public async Task UnsupportedProfileNeverWrites(RestorationProfileState state)
    {
        using var f = new RestorationLoopFixture { Profile = state }; await f.InitializeAsync();
        Assert.Equal(RestorationScanStatus.Ineligible, (await f.Loop.ScanAsync()).Status); Assert.Equal(0, f.Registry.Writes);
    }
    [Theory]
    [InlineData(RestorationManagementState.Unknown)]
    [InlineData(RestorationManagementState.Managed)]
    public async Task ManagementBlocks(RestorationManagementState state)
    {
        using var f = new RestorationLoopFixture { Management = state }; await f.InitializeAsync();
        Assert.Equal(RestorationScanStatus.Ineligible, (await f.Loop.ScanAsync()).Status); Assert.Equal(0, f.Registry.Writes);
    }
    [Fact]
    public async Task OffOrMissingBaselineNeverWrites()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync(false);
        Assert.Equal(RestorationScanStatus.BaselineMissing, (await f.Loop.ScanAsync()).Status);
        f.Consent.Enabled = false;
        Assert.Equal(RestorationScanStatus.ConsentOff, (await f.Loop.ScanAsync()).Status); Assert.Equal(0, f.Registry.Writes);
    }
    [Fact]
    public async Task ReadFailureDoesNotBecomeAbsenceOrAWrite()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync(); f.Registry.ReadError = ErrorCategory.AccessDenied;
        Assert.Equal(RestorationScanStatus.ReadFailed, (await f.Loop.ScanAsync()).Status); Assert.Equal(0, f.Registry.Writes);
        f.Registry.ReadError = null; f.Registry.Value = null;
        Assert.Equal(RestorationScanStatus.UnsupportedObservation, (await f.Loop.ScanAsync()).Status); Assert.Equal(0, f.Registry.Writes);
    }
    [Fact]
    public async Task WriteFailureRemainsUncertainEvenWhenRereadMatchesBefore()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync(); f.Registry.FailWrite = true;
        Assert.Equal(RestorationScanStatus.Uncertain, (await f.Loop.ScanAsync()).Status);
        Assert.Equal(RestorationScanStatus.RecoveryRequired, (await f.Loop.ScanAsync()).Status);
        Assert.Equal(1, f.Registry.Writes);
        Assert.All(await f.Repository.GetAllAsync(), row => Assert.Equal("Uncertain", row.JournalOutcome));
    }
    [Fact]
    public async Task RepeatedSuccessfulWritesReachSeparateConservativeCap()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync();
        for (var i = 0; i < 3; i++)
        {
            f.Registry.Value = f.Target.WindowsDefaultValue;
            Assert.Equal(RestorationScanStatus.Restored, (await f.Loop.ScanAsync()).Status);
        }
        f.Registry.Value = f.Target.WindowsDefaultValue;
        Assert.Equal(RestorationScanStatus.AttemptLimitReached, (await f.Loop.ScanAsync()).Status); Assert.Equal(3, f.Registry.Writes);
    }
    [Fact]
    public async Task UnmatchedDurableIntentBlocksRestartEvenWhenLiveValueMatches()
    {
        using var f = new RestorationLoopFixture();
        await f.InitializeAsync();
        var acquisition = await f.Provider.AcquireAsync(TimeSpan.Zero);
        await using (var lease = acquisition.Lease!)
        {
            lease.MarkRecovered();
            var entry = Assert.Single(f.Baseline.Read(lease));
            var candidate = RestorationCatalog.Default.Validate(new DriftBaselineEntry
            {
                ModuleId = entry.ModuleId, SettingId = entry.SettingId, DisplayName = f.Target.DisplayName,
                SystemLocation = entry.CanonicalLocation, ExpectedValue = entry.Expected.Data,
                ValueType = f.Target.ValueType, UpdatedAtUtc = entry.UpdatedAtUtc,
            }, RestorationLoopFixture.Sid).Candidate!;
            Assert.NotNull(f.Journal.Begin(Guid.NewGuid(), candidate,
                RegistryValueSnapshot.Present(f.Target.WindowsDefaultValue), lease).Permit);
        }
        // The observed value is not evidence that the interrupted writer completed.
        f.Registry.Value = f.Target.SuppressedValue;
        await f.InitializeAsync(false);
        Assert.Equal(RestorationScanStatus.RecoveryRequired, (await f.Loop.ScanAsync()).Status);
        Assert.Equal(0, f.Registry.Writes);
        Assert.Empty(await f.Repository.GetAllAsync());
    }

    [Fact]
    public async Task CompletedUncertainOutcomeRetriesHistoryImportAfterDatabaseFailure()
    {
        using var f = new RestorationLoopFixture();
        await f.InitializeAsync();
        await Sql("CREATE TRIGGER refuse_receipt BEFORE INSERT ON owner_journal_imports BEGIN SELECT RAISE(ABORT, 'test'); END;");
        f.Registry.FailWrite = true;
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => f.Loop.ScanAsync());
        Assert.Empty(await f.Repository.GetAllAsync());
        Assert.Equal(1, f.Registry.Writes);
        await Sql("DROP TRIGGER refuse_receipt;");
        Assert.Equal(RestorationScanStatus.RecoveryRequired, (await f.Loop.ScanAsync()).Status);
        Assert.Equal("Uncertain", Assert.Single(await f.Repository.GetAllAsync()).JournalOutcome);
        Assert.Equal(RestorationScanStatus.RecoveryRequired, (await f.Loop.ScanAsync()).Status);
        Assert.Single(await f.Repository.GetAllAsync());
        Assert.Equal(1, f.Registry.Writes);
        async Task Sql(string text)
        {
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={Path.Combine(f.DirectoryPath, "history.db")}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = text;
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task JournalAndAttemptBudgetShareInjectedClock()
    {
        var clock = new LoopClock { Now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        using var f = new RestorationLoopFixture(clock);
        await f.InitializeAsync();
        for (var i = 0; i < 3; i++)
        {
            f.Registry.Value = f.Target.WindowsDefaultValue;
            Assert.Equal(RestorationScanStatus.Restored, (await f.Loop.ScanAsync()).Status);
        }
        f.Registry.Value = f.Target.WindowsDefaultValue;
        Assert.Equal(RestorationScanStatus.AttemptLimitReached, (await f.Loop.ScanAsync()).Status);
        clock.Now += TimeSpan.FromDays(7) + TimeSpan.FromTicks(1);
        Assert.Equal(RestorationScanStatus.Restored, (await f.Loop.ScanAsync()).Status);
        Assert.Equal(4, f.Registry.Writes);
    }

    private sealed class LoopClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task FakeTicksRunBoundedScansWithoutDelay()
    {
        using var f = new RestorationLoopFixture(); await f.InitializeAsync(); var results = new List<RestorationScanResult>();
        var ticks = 0;
        await f.Loop.RunAsync(result => { results.Add(result); return Task.CompletedTask; }, _ => Task.FromResult(++ticks < 2));
        Assert.Equal(2, results.Count); Assert.Equal(1, f.Registry.Writes);
    }
}
