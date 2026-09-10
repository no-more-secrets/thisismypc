using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Data.Sqlite;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Journal;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Drift.Journal;

namespace ThisIsMyPC.Security.Tests.Drift.Journal;

/// <summary>Elevated tests use isolated temporary storage and never change production settings.</summary>
[Trait("Category", "Integration")]
public sealed class TrustedRestorationStorageTests
{
    private sealed class Lease() : MutationLeaseBase("native-journal-test", "test", false)
    {
        protected override void ReleaseCore() { }
    }

    private sealed class NonRepairGuard(TrustedRestorationStorage scope) : IDataDirectoryGuard
    {
        public OperationResult<DaclStatus> EnsureHardened(string directoryPath) =>
            scope.CheckJournalPath(directoryPath)
                ? OperationResult<DaclStatus>.Success(DaclStatus.Verified)
                : OperationResult<DaclStatus>.Success(DaclStatus.Failed);
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "tipc-storage-tests", Guid.NewGuid().ToString("N"));
        public string Data => Path.Combine(Root, "data");
        public string Journal => Path.Combine(Root, "journal");
        public Fixture()
        {
            using var identity = WindowsIdentity.GetCurrent();
            Assert.True(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), "Run storage integration tests elevated.");
            foreach (var path in new[] { Data, Journal })
            {
                Directory.CreateDirectory(path);
                var security = new DirectorySecurity();
                security.SetSecurityDescriptorSddlForm("O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)");
                new DirectoryInfo(path).SetAccessControl(security);
            }
        }
        public void Dispose() => Directory.Delete(Root, true);
    }

    [Fact]
    public void JournalPredicatePinsInheritedFileWithoutBlockingAppend()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Journal, Guid.NewGuid().ToString("N") + ".tipj");
        File.WriteAllText(path, "before");
        using (var scope = new TrustedRestorationStorage(fixture.Data, fixture.Journal))
        {
            Assert.True(scope.CheckJournalPath(path));
            Assert.False(scope.CheckJournalPath(Path.Combine(fixture.Data, "outside.tipj")));
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read))
            {
                writer.Position = writer.Length;
                writer.Write("after"u8);
            }
            Assert.ThrowsAny<IOException>(() => File.Move(path, path + ".moved"));
            Assert.ThrowsAny<IOException>(() => Directory.Move(fixture.Journal, fixture.Journal + "-moved"));
        }
        Assert.Equal("beforeafter", File.ReadAllText(path));
    }

    [Fact]
    public void RealJournalIntentOutcomeAndReceiptSurviveNativeScopeReopen()
    {
        using var fixture = new Fixture();
        using var lease = new Lease();
        lease.MarkRecovered();
        var target = RestorationCatalog.Default.Targets[0];
        var candidate = RestorationCatalog.Default.Validate(new DriftBaselineEntry
        {
            ModuleId = target.ModuleId, SettingId = target.SettingId, DisplayName = target.DisplayName,
            SystemLocation = target.KeyPath + "\\" + target.ValueName, ValueType = target.ValueType,
            ExpectedValue = target.SuppressedValue.Data, UpdatedAtUtc = DateTimeOffset.UtcNow,
        }, "S-1-5-21-111-222-333-1001").Candidate!;
        var id = Guid.NewGuid();
        using (var scope = new TrustedRestorationStorage(fixture.Data, fixture.Journal))
        {
            var journal = new RestorationJournal(scope.JournalPath, new NonRepairGuard(scope), scope.CheckJournalPath);
            var permit = journal.Begin(id, candidate, RegistryValueSnapshot.Present(target.WindowsDefaultValue), lease).Permit!;
            Assert.Null(Assert.Single(journal.Read(lease).Attempts).Outcome);
            journal.Complete(permit, new JournalOutcome(JournalOutcomeKind.Applied, candidate.DesiredValue, "Test verified"), lease);
            journal.AcknowledgeImported(id, "test-receipt", lease);
        }
        using var reopened = new TrustedRestorationStorage(fixture.Data, fixture.Journal);
        var restored = new RestorationJournal(reopened.JournalPath, new NonRepairGuard(reopened), reopened.CheckJournalPath);
        var attempt = Assert.Single(restored.Read(lease).Attempts);
        Assert.Equal(JournalOutcomeKind.Applied, attempt.Outcome!.Kind);
        Assert.Equal("test-receipt", attempt.ImportTransactionId);
        Assert.Null(restored.Begin(id, candidate, RegistryValueSnapshot.Present(target.WindowsDefaultValue), lease).Permit);
    }

    [Fact]
    public void SqlitePersistCreatesAndReopensDatabaseWithHeldScope()
    {
        using var fixture = new Fixture();
        using var scope = new TrustedRestorationStorage(fixture.Data, fixture.Journal);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = scope.HistoryPath, Pooling = false }.ToString();
        for (var iteration = 0; iteration < 2; iteration++)
        {
            scope.VerifyHistoryFiles();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=PERSIST; CREATE TABLE IF NOT EXISTS receipt (id INTEGER); INSERT INTO receipt VALUES (1);";
            command.ExecuteNonQuery();
            command.CommandText = "SELECT COUNT(*) FROM receipt";
            Assert.Equal(iteration + 1L, (long)command.ExecuteScalar()!);
        }
        scope.VerifyHistoryFiles();
        Assert.ThrowsAny<IOException>(() => File.Move(scope.HistoryPath, scope.HistoryPath + ".moved"));
    }

    [Fact]
    public void UnsafeFileIsRefusedWithoutRepair()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Data, "history.db");
        File.WriteAllText(path, "unchanged");
        var security = new FileSecurity();
        security.SetSecurityDescriptorSddlForm("O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;BU)");
        new FileInfo(path).SetAccessControl(security);
        Assert.ThrowsAny<IOException>(() => new TrustedRestorationStorage(fixture.Data, fixture.Journal));
        Assert.Equal("unchanged", File.ReadAllText(path));
        Assert.Equal(3, new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Count);
    }

    [Fact]
    public void ReparseChildAndWalRequireRefusal()
    {
        using var fixture = new Fixture();
        var target = Path.Combine(fixture.Root, "target");
        File.WriteAllText(target, "unchanged");
        var link = Path.Combine(fixture.Data, "history.db");
        File.CreateSymbolicLink(link, target);
        Assert.ThrowsAny<IOException>(() => new TrustedRestorationStorage(fixture.Data, fixture.Journal));
        File.Delete(link);
        File.WriteAllText(link + "-wal", "evidence");
        Assert.ThrowsAny<IOException>(() => new TrustedRestorationStorage(fixture.Data, fixture.Journal));
        Assert.Equal("evidence", File.ReadAllText(link + "-wal"));
    }
}
