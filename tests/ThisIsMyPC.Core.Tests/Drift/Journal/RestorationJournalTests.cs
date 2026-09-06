using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Journal;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Drift.Journal;

public sealed class RestorationJournalTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tipc-journal-tests", Guid.NewGuid().ToString("N"));
    private readonly Lease _lease = new();
    public RestorationJournalTests() { Directory.CreateDirectory(_directory); _lease.MarkRecovered(); }
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

    [Fact]
    public void IntentIsReopenedBeforeWrite_AndUnmatchedIntentIsNeverUndo()
    {
        var journal = Open();
        var id = Guid.NewGuid();
        var result = journal.Begin(id, Candidate(), Before(), _lease);
        Assert.False(result.AlreadyExists);
        Assert.NotNull(result.Permit);
        var intent = Assert.Single(Open().Read(_lease).Attempts);
        Assert.Null(intent.Outcome);
        Assert.True(intent.IsUncertain);
        Assert.False(intent.CanImportOrdinaryUndo);
        Assert.StartsWith("HKCU\\", intent.Intent.CanonicalLocation, StringComparison.Ordinal);
        Assert.Equal(Candidate().UserSid, intent.Intent.UserSid);
        Assert.Equal(_lease.LeaseId, intent.Intent.LeaseId);
    }

    [Fact]
    public void OutcomeAndAcknowledgementSurviveReopen_AndRetriesDoNotAppend()
    {
        var journal = Open();
        var id = Guid.NewGuid();
        var permit = journal.Begin(id, Candidate(), Before(), _lease).Permit!;
        journal.Complete(permit, Applied(), _lease);
        var length = new FileInfo(Directory.GetFiles(_directory).Single()).Length;
        journal.Complete(permit, Applied(), _lease);
        Assert.Equal(length, new FileInfo(Directory.GetFiles(_directory).Single()).Length);
        Assert.True(Assert.Single(Open().Read(_lease).Attempts).CanImportOrdinaryUndo);
        journal.AcknowledgeImported(id, "history-transaction-1", _lease);
        journal.AcknowledgeImported(id, "history-transaction-1", _lease);
        var attempt = Assert.Single(Open().Read(_lease).Attempts);
        Assert.Equal("history-transaction-1", attempt.ImportTransactionId);
        Assert.False(attempt.CanImportOrdinaryUndo);
        Assert.Single(Directory.GetFiles(_directory));
        Assert.Throws<InvalidOperationException>(() => journal.AcknowledgeImported(id, "different", _lease));
    }

    [Fact]
    public void DuplicateIntentNeverGrantsSecondWritePermit_EvenAfterReopen()
    {
        var id = Guid.NewGuid();
        Open().Begin(id, Candidate(), Before(), _lease);
        var duplicate = Open().Begin(id, Candidate(), Before(), _lease);
        Assert.True(duplicate.AlreadyExists);
        Assert.Null(duplicate.Permit);
        Assert.Single(Open().Read(_lease).Attempts);
        var different = Candidate() with { UserSid = "S-1-5-21-111-222-333-1002",
            ResolvedKeyPath = Candidate().ResolvedKeyPath.Replace("1001", "1002", StringComparison.Ordinal) };
        Assert.Throws<InvalidOperationException>(() => Open().Begin(id, different, Before(), _lease));
    }

    [Fact]
    public void RecoveryMatchIsDiagnostic_NotAppliedOrUndo()
    {
        var id = Guid.NewGuid();
        Open().Begin(id, Candidate(), Before(), _lease);
        var journal = Open();
        journal.RecordRecoveryObservation(id, RegistryValueSnapshot.Present(Candidate().DesiredValue), "Value matches; writer unknown", _lease);
        var attempt = Assert.Single(journal.Read(_lease).Attempts);
        Assert.Equal(JournalOutcomeKind.RecoveryObservation, attempt.Outcome!.Kind);
        Assert.False(attempt.CanImportOrdinaryUndo);
        journal.AcknowledgeImported(id, "diagnostic-import", _lease);
    }

    [Fact]
    public void AbsentRecoveryValueRemainsAbsent()
    {
        var id = Guid.NewGuid();
        Open().Begin(id, Candidate(), Before(), _lease);
        Open().RecordRecoveryObservation(id, RegistryValueSnapshot.Absent, "Deleted", _lease);
        Assert.Null(Assert.Single(Open().Read(_lease).Attempts).Outcome!.Observed);
    }

    [Fact]
    public void CapacityRefusesNewIntents_ButReservesOutcomeAndAck_WithoutDeletingRecords()
    {
        var journal = Open(RestorationJournal.AttemptReservationBytes);
        var id = Guid.NewGuid();
        var permit = journal.Begin(id, Candidate(), Before(), _lease).Permit!;
        Assert.Throws<IOException>(() => journal.Begin(Guid.NewGuid(), Candidate(), Before(), _lease));
        journal.Complete(permit, Applied(), _lease);
        journal.AcknowledgeImported(id, "imported", _lease);
        Assert.Throws<IOException>(() => journal.Begin(Guid.NewGuid(), Candidate(), Before(), _lease));
        Assert.Single(journal.Read(_lease).Attempts);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(33)]
    public void TornOutcomeKeepsIntentDiagnostic_AndBlocksEveryAppend(int missingBytes)
    {
        var journal = Open();
        var id = Guid.NewGuid();
        var permit = journal.Begin(id, Candidate(), Before(), _lease).Permit!;
        journal.Complete(permit, Applied(), _lease);
        using (var file = File.OpenWrite(Directory.GetFiles(_directory).Single())) file.SetLength(file.Length - missingBytes);
        var recovered = Open().Read(_lease);
        Assert.False(recovered.IsHealthy);
        Assert.Null(Assert.Single(recovered.Attempts).Outcome);
        Assert.False(recovered.Attempts[0].CanImportOrdinaryUndo);
        Assert.Throws<InvalidDataException>(() => journal.Complete(permit, Applied(), _lease));
        Assert.Throws<InvalidDataException>(() => journal.Begin(Guid.NewGuid(), Candidate(), Before(), _lease));
    }

    [Fact]
    public void ChecksumCorruptionFailsClosed_WithoutReturningAnImportableAttempt()
    {
        var journal = Open();
        journal.Begin(Guid.NewGuid(), Candidate(), Before(), _lease);
        var path = Directory.GetFiles(_directory).Single();
        var bytes = File.ReadAllBytes(path);
        bytes[12] ^= 1;
        File.WriteAllBytes(path, bytes);
        var snapshot = journal.Read(_lease);
        Assert.False(snapshot.IsHealthy);
        Assert.Empty(snapshot.Attempts);
        Assert.Throws<InvalidDataException>(() => journal.Begin(Guid.NewGuid(), Candidate(), Before(), _lease));
    }

    [Fact]
    public void TrustAndHardeningAreRequired_EvenForReads()
    {
        Assert.Throws<UnauthorizedAccessException>(() => Open(trust: _ => false).Read(_lease));
        Assert.Throws<UnauthorizedAccessException>(() => Open(hardened: false).Begin(Guid.NewGuid(), Candidate(), Before(), _lease));
        Open().Begin(Guid.NewGuid(), Candidate(), Before(), _lease);
        Assert.Throws<UnauthorizedAccessException>(() => Open(trust: p => p == _directory).Read(_lease));
    }

    [Fact]
    public void UnrecoveredLeaseCanRead_ButCannotWrite_AndReleasedLeaseCannotRead()
    {
        using var lease = new Lease();
        var journal = Open();
        Assert.True(journal.Read(lease).IsHealthy);
        Assert.True(lease.RequiresRecovery);
        Assert.Throws<InvalidOperationException>(() => journal.Begin(Guid.NewGuid(), Candidate(), Before(), lease));
        lease.Dispose();
        Assert.Throws<InvalidOperationException>(() => journal.Read(lease));
    }

    [Fact]
    public void OutcomeRequiresOriginalLivePermit_AndMatchingObservedSuccess()
    {
        var journal = Open();
        var permit = journal.Begin(Guid.NewGuid(), Candidate(), Before(), _lease).Permit!;
        using var otherLease = new Lease();
        otherLease.MarkRecovered();
        Assert.Throws<InvalidOperationException>(() => journal.Complete(permit, Applied(), otherLease));
        Assert.Throws<InvalidOperationException>(() => Open().Complete(permit, Applied(), _lease));
        Assert.Throws<InvalidDataException>(() => journal.Complete(permit, new(JournalOutcomeKind.Applied, null, "not observed"), _lease));
        Assert.Throws<InvalidOperationException>(() => journal.AcknowledgeImported(permit.Intent.AttemptId, "premature", _lease));
    }

    [Fact]
    public void ForgedCandidateCannotCreateIntent()
    {
        Assert.Throws<InvalidOperationException>(() => Open().Begin(Guid.NewGuid(),
            Candidate() with { ResolvedKeyPath = "HKLM\\Arbitrary" }, Before(), _lease));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void ExclusiveFileFailureDoesNotInventAnOutcome()
    {
        var journal = Open();
        var permit = journal.Begin(Guid.NewGuid(), Candidate(), Before(), _lease).Permit!;
        using (File.Open(Directory.GetFiles(_directory).Single(), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => journal.Complete(permit, Applied(), _lease));
        Assert.Null(Assert.Single(Open().Read(_lease).Attempts).Outcome);
    }

    [Fact]
    public void TornAcknowledgementMakesTheCompletedPrefixDiagnosticOnly()
    {
        var journal = Open();
        var id = Guid.NewGuid();
        var permit = journal.Begin(id, Candidate(), Before(), _lease).Permit!;
        journal.Complete(permit, Applied(), _lease);
        journal.AcknowledgeImported(id, "committed-history", _lease);
        using (var file = File.OpenWrite(Directory.GetFiles(_directory).Single())) file.SetLength(file.Length - 1);
        var snapshot = Open().Read(_lease);
        Assert.False(snapshot.IsHealthy);
        var prefix = Assert.Single(snapshot.Attempts);
        Assert.NotNull(prefix.Outcome);
        Assert.True(prefix.DiagnosticOnly);
        Assert.False(prefix.CanImportOrdinaryUndo);
    }

    [Theory]
    [InlineData(JournalOutcomeKind.Failed)]
    [InlineData(JournalOutcomeKind.Uncertain)]
    public void NonSuccessNeverBecomesUndo_AndCannotBeReplacedWithSuccess(JournalOutcomeKind kind)
    {
        var journal = Open();
        var permit = journal.Begin(Guid.NewGuid(), Candidate(), Before(), _lease).Permit!;
        journal.Complete(permit, new(kind, Candidate().DesiredValue, "May have written"), _lease);
        Assert.False(Assert.Single(journal.Read(_lease).Attempts).CanImportOrdinaryUndo);
        Assert.Throws<InvalidOperationException>(() => journal.Complete(permit, Applied(), _lease));
    }
    [Fact]
    public void FreshUnrecoveredLeasePersistsRecoveryEvidenceBeforeOrdinaryWritesAreAllowed()
    {
        var journal = Open();
        var id = Guid.NewGuid();
        var permit = journal.Begin(id, Candidate(), Before(), _lease).Permit!;
        _lease.Dispose();
        using var recovering = new Lease();
        // A failing guard proves recovery does not try to repair directory DACLs.
        var reopened = Open(hardened: false);
        Assert.Null(Assert.Single(reopened.Read(recovering).Attempts).Outcome);
        reopened.RecordRecoveryObservation(id, RegistryValueSnapshot.Present(Candidate().DesiredValue),
            "Found after restart; writer unknown", recovering);
        Assert.True(recovering.RequiresRecovery);
        Assert.False(recovering.CanWrite);
        var recovered = Assert.Single(Open().Read(recovering).Attempts);
        Assert.Equal(JournalOutcomeKind.RecoveryObservation, recovered.Outcome!.Kind);
        Assert.False(recovered.CanImportOrdinaryUndo);
        Assert.Throws<InvalidOperationException>(() => journal.Begin(Guid.NewGuid(), Candidate(), Before(), recovering));
        Assert.Throws<InvalidOperationException>(() => journal.Complete(permit, Applied(), recovering));
        Assert.Throws<InvalidOperationException>(() => journal.AcknowledgeImported(id, "too-soon", recovering));
        recovering.MarkRecovered();
        Assert.NotNull(journal.Begin(Guid.NewGuid(), Candidate(), Before(), recovering).Permit);
        recovering.Dispose();
        Assert.Throws<InvalidOperationException>(() => reopened.RecordRecoveryObservation(id,
            RegistryValueSnapshot.Absent, "released", recovering));
    }
    public void Dispose() { _lease.Dispose(); Directory.Delete(_directory, recursive: true); }
    private sealed class Lease() : MutationLeaseBase("test-journal", "test", false)
    { protected override void ReleaseCore() { } }
    private sealed class Guard(bool success) : IDataDirectoryGuard
    {
        public OperationResult<DaclStatus> EnsureHardened(string directoryPath) => success
            ? OperationResult<DaclStatus>.Success(DaclStatus.Verified)
            : OperationResult<DaclStatus>.Failure("Refused", ErrorCategory.AccessDenied);
    }
}
