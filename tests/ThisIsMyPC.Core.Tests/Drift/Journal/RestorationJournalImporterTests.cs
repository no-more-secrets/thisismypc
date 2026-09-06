using Microsoft.Data.Sqlite;
using ThisIsMyPC.Core.Data;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Journal;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Drift.Journal;

public sealed class RestorationJournalImporterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tipc-journal-tests", Guid.NewGuid().ToString("N"));
    private readonly Lease _lease = new();
    public RestorationJournalImporterTests() { Directory.CreateDirectory(_directory); _lease.MarkRecovered(); }
    private RestorationJournal Open(long maximum = 16 * 1024 * 1024, Func<string, bool>? trust = null, bool hardened = true)
        => new(_directory, new Guard(hardened), trust ?? (_ => true), maximum);
    private static RestorationCandidate Candidate()
    {
        var target = RestorationCatalog.Default.Targets[0];
        return RestorationCatalog.Default.Validate(new DriftBaselineEntry
        {
            ModuleId = target.ModuleId, SettingId = target.SettingId, DisplayName = target.DisplayName,
            SystemLocation = target.KeyPath + "\\" + target.ValueName, ValueType = target.ValueType,
            ExpectedValue = target.SuppressedValue.Data, UpdatedAtUtc = DateTimeOffset.UtcNow,
        }, "S-1-5-21-111-222-333-1001").Candidate!;
    }
    private static RegistryValueSnapshot Before() => RegistryValueSnapshot.Present(Candidate().Target.WindowsDefaultValue);
    private static JournalOutcome Applied() => new(JournalOutcomeKind.Applied, Candidate().DesiredValue, "Verified by writer");

    private async Task<ChangeHistoryRepository> Repository()
    {
        var repository = new ChangeHistoryRepository();
        await repository.InitializeDatabaseAsync(Path.Combine(_directory, "history.db"));
        return repository;
    }
    private RestorationJournal Journal()
    {
        var path = Path.Combine(_directory, "journal");
        Directory.CreateDirectory(path);
        return new(path, new Guard(true), _ => true);
    }
    private void Complete(RestorationJournal journal, Guid id, JournalOutcomeKind kind = JournalOutcomeKind.Applied)
    {
        var permit = journal.Begin(id, Candidate(), Before(), _lease).Permit!;
        journal.Complete(permit, new(kind, kind == JournalOutcomeKind.Applied ? Candidate().DesiredValue : null, "Evidence"), _lease);
    }
    [Fact]
    public async Task DuplicateAndClearPreserveReceiptAndCanonicalIdentity()
    {
        var repository = await Repository();
        var journal = Journal();
        var id = Guid.NewGuid();
        Complete(journal, id);
        var importer = new RestorationJournalImporter(repository);
        await importer.ImportAsync(journal, _lease);
        await importer.ImportAsync(journal, _lease);
        var row = Assert.Single(await repository.GetAllAsync());
        Assert.Equal(id, row.OwnerAttemptId);
        Assert.Equal(Candidate().UserSid, row.TargetUserSid);
        Assert.StartsWith("HKCU", row.SystemLocation);
        Assert.False(row.SupportsGenericUndo);
        await repository.DeleteAllAsync();
        await importer.ImportAsync(journal, _lease);
        Assert.Empty(await repository.GetAllAsync());
    }
    [Theory]
    [InlineData(JournalOutcomeKind.Applied)]
    [InlineData(JournalOutcomeKind.Failed)]
    [InlineData(JournalOutcomeKind.Uncertain)]
    [InlineData(JournalOutcomeKind.RecoveryObservation)]
    public async Task ImportedEvidenceNeverCallsGenericExecution(JournalOutcomeKind kind)
    {
        var repository = await Repository();
        var journal = Journal();
        var id = Guid.NewGuid();
        if (kind == JournalOutcomeKind.RecoveryObservation)
        {
            journal.Begin(id, Candidate(), Before(), _lease);
            journal.RecordRecoveryObservation(id, RegistryValueSnapshot.Absent, "Absent", _lease);
        }
        else Complete(journal, id, kind);
        await new RestorationJournalImporter(repository).ImportAsync(journal, _lease);
        var row = Assert.Single(await repository.GetAllAsync());
        Assert.Equal(kind.ToString(), row.JournalOutcome);
        var service = new ChangeHistoryService(repository);
        Task<OperationResult<bool>> Forbidden(ChangeDescriptor _) => throw new InvalidOperationException("Writer called");
        Assert.False((await service.RevertChangeAsync(row.Id, Forbidden)).IsSuccess);
        await repository.UpdateRevertedAtAsync(row.Id, DateTimeOffset.UtcNow, row.Id);
        Assert.False((await service.RedoChangeAsync(row.Id, Forbidden)).IsSuccess);
    }
    [Fact]
    public async Task CommitBeforeAcknowledgementCanRetryWithoutDuplicate()
    {
        var repository = await Repository();
        var journal = Journal();
        Complete(journal, Guid.NewGuid());
        var path = Directory.GetDirectories(_directory).Single();
        var guarded = new RestorationJournal(path, new Guard(false), _ => true);
        var importer = new RestorationJournalImporter(repository);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => importer.ImportAsync(guarded, _lease));
        Assert.Single(await repository.GetAllAsync());
        Assert.Null(Assert.Single(journal.Read(_lease).Attempts).ImportTransactionId);
        await importer.ImportAsync(journal, _lease);
        Assert.Single(await repository.GetAllAsync());
        Assert.NotNull(Assert.Single(journal.Read(_lease).Attempts).ImportTransactionId);
    }
    [Fact]
    public async Task ConflictingReceiptAndTransactionFailureFailClosed()
    {
        var repository = await Repository();
        var journal = Journal();
        Complete(journal, Guid.NewGuid());
        using var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "history.db")}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER refuse_receipt BEFORE INSERT ON owner_journal_imports BEGIN SELECT RAISE(ABORT, 'test'); END;";
        await command.ExecuteNonQueryAsync();
        var importer = new RestorationJournalImporter(repository);
        await Assert.ThrowsAsync<SqliteException>(() => importer.ImportAsync(journal, _lease));
        Assert.Empty(await repository.GetAllAsync());
        command.CommandText = "DROP TRIGGER refuse_receipt";
        await command.ExecuteNonQueryAsync();
        await importer.ImportAsync(journal, _lease);
        command.CommandText = "UPDATE owner_journal_imports SET payload_json='conflict'";
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportAsync(journal, _lease));
    }
    [Fact]
    public async Task UnmatchedAndCorruptJournalCannotImport()
    {
        var repository = await Repository();
        var journal = Journal();
        journal.Begin(Guid.NewGuid(), Candidate(), Before(), _lease);
        var importer = new RestorationJournalImporter(repository);
        Assert.Equal(0, await importer.ImportAsync(journal, _lease));
        var file = Directory.GetFiles(Path.Combine(_directory, "journal")).Single();
        await File.AppendAllTextAsync(file, "torn");
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportAsync(journal, _lease));
        Assert.Empty(await repository.GetAllAsync());
    }
    [Fact]
    public async Task VersionTwoMigrationPreservesRows()
    {
        var repository = await Repository();
        await repository.InsertAsync(new ChangeHistoryEntry
        {
            ModuleId = "test", SettingId = "test", DisplayName = "Old row", SystemLocation = "HKCU\\test",
            ValueType = ChangeValueType.Registry_DWord, AppliedAt = DateTimeOffset.UtcNow,
        });
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "history.db")}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DROP INDEX ix_history_owner_attempt;
                ALTER TABLE change_history DROP COLUMN owner_attempt_id;
                ALTER TABLE change_history DROP COLUMN target_user_sid;
                ALTER TABLE change_history DROP COLUMN journal_outcome;
                ALTER TABLE change_history DROP COLUMN journal_detail;
                DROP TABLE owner_journal_imports;
                DELETE FROM schema_version;
                INSERT INTO schema_version VALUES(2,'2026-01-01');
                """;
            await command.ExecuteNonQueryAsync();
        }
        repository = await Repository();
        Assert.Equal("Old row", Assert.Single(await repository.GetAllAsync()).DisplayName);
        var journal = Journal();
        Complete(journal, Guid.NewGuid());
        await new RestorationJournalImporter(repository).ImportAsync(journal, _lease);
        Assert.Equal(2, await repository.GetEntryCountAsync());
    }
    [Fact]
    public async Task SeparateProfilesKeepSeparateIdentity()
    {
        var repository = await Repository();
        var journal = Journal();
        Complete(journal, Guid.NewGuid());
        var original = Candidate();
        var sid = "S-1-5-21-111-222-333-1002";
        var other = original with { UserSid = sid, ResolvedKeyPath = original.ResolvedKeyPath.Replace(original.UserSid!, sid) };
        var permit = journal.Begin(Guid.NewGuid(), other, Before(), _lease).Permit!;
        journal.Complete(permit, Applied(), _lease);
        await new RestorationJournalImporter(repository).ImportAsync(journal, _lease);
        var rows = await repository.GetAllAsync();
        Assert.Equal(2, rows.Select(r => r.TargetUserSid).Distinct().Count());
        Assert.Single(rows.Select(r => r.SystemLocation).Distinct());
    }
    public void Dispose() { _lease.Dispose(); SqliteConnection.ClearAllPools(); Directory.Delete(_directory, recursive: true); }
    private sealed class Lease() : MutationLeaseBase("test-journal", "test", false)
    { protected override void ReleaseCore() { } }
    private sealed class Guard(bool success) : IDataDirectoryGuard
    {
        public OperationResult<DaclStatus> EnsureHardened(string directoryPath) => success
            ? OperationResult<DaclStatus>.Success(DaclStatus.Verified)
            : OperationResult<DaclStatus>.Failure("Refused", ErrorCategory.AccessDenied);
    }
}
