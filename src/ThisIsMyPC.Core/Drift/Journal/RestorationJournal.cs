using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Drift.Journal;

/// <summary>
/// Append-only intent, outcome, and import acknowledgement files. The caller holds
/// the mutation lease through journal and system writes. No system writer lives here.
/// Existing directory hardening and a mandatory trust check are separate gates.
/// </summary>
public sealed class RestorationJournal
{
    public const int MaximumPayloadBytes = 16 * 1024;
    public const int AttemptReservationBytes = 3 * (MaximumPayloadBytes + 40);
    private const uint Magic = 0x314A5054;
    private readonly string _directory;
    private readonly IDataDirectoryGuard _guard;
    private readonly Func<string, bool> _trustCheck;
    private readonly long _maximumBytes;
    private readonly Lock _gate = new();

    public RestorationJournal(string directory, IDataDirectoryGuard guard,
        Func<string, bool> trustCheck, long maximumBytes = 16 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(trustCheck);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, AttemptReservationBytes);
        _directory = Path.GetFullPath(directory);
        _guard = guard;
        _trustCheck = trustCheck;
        _maximumBytes = maximumBytes;
    }

    /// <summary>Read during recovery before MarkRecovered. Never marks a lease recovered automatically.</summary>
    public JournalSnapshot Read(IMutationLease lease)
    {
        lock (_gate)
        {
            CheckLease(lease, writing: false);
            CheckDirectory(harden: false);
            return ReadCore();
        }
    }

    /// <summary>
    /// Flushes a new intent before granting a permit. Existing IDs return no permit,
    /// including unmatched intents from a previous process. No retry can repeat a write.
    /// </summary>
    public BeginJournalAttempt Begin(Guid attemptId, RestorationCandidate candidate,
        RegistryValueSnapshot observed, IMutationLease lease)
    {
        lock (_gate)
        {
            CheckLease(lease, writing: true);
            CheckDirectory();
            if (attemptId == Guid.Empty) throw new ArgumentException("Attempt id is required.", nameof(attemptId));
            var prepared = RestorationBatchFactory.Prepare(candidate, observed);
            if (!prepared.IsReady) throw new InvalidOperationException(prepared.Detail);
            var intent = new JournalIntent(attemptId, lease.LeaseId, DateTimeOffset.UtcNow,
                candidate.Target.ModuleId, candidate.Target.SettingId,
                candidate.Target.KeyPath + "\\" + candidate.Target.ValueName, candidate.UserSid!,
                new RegistryValueData(RegistryValueDataKind.DWord, prepared.Change!.BeforeValue), candidate.DesiredValue);
            var snapshot = HealthySnapshot();
            var existing = snapshot.Attempts.SingleOrDefault(a => a.Intent.AttemptId == attemptId);
            if (existing is not null)
            {
                if (existing.Intent with { LeaseId = intent.LeaseId, CreatedAt = intent.CreatedAt } != intent)
                    throw new InvalidOperationException("Attempt id already belongs to different intent data.");
                return new BeginJournalAttempt(true, null);
            }
            // Reserve all three maximum-size frames, not just today's short intent.
            // An accepted attempt can still record failure and acknowledgement at capacity.
            if ((snapshot.Attempts.Count + 1L) * AttemptReservationBytes > _maximumBytes)
                throw new IOException("Journal storage pressure: new attempts are refused; retained records were not deleted.");
            Append(new JournalRecord(1, "intent", attemptId, Intent: intent), lease, create: true);
            return new BeginJournalAttempt(false, new DurableIntent(this, intent));
        }
    }

    /// <summary>Flush the result after the system write, while the same acquisition remains held.</summary>
    public void Complete(DurableIntent permit, JournalOutcome outcome, IMutationLease lease)
    {
        ArgumentNullException.ThrowIfNull(permit);
        lock (_gate)
        {
            CheckLease(lease, writing: true);
            if (!ReferenceEquals(permit.Issuer, this) || permit.Intent.LeaseId != lease.LeaseId)
                throw new InvalidOperationException("Permit belongs to another journal or lease acquisition.");
            if (outcome?.Kind == JournalOutcomeKind.RecoveryObservation)
                throw new ArgumentException("Use RecordRecoveryObservation for recovery.", nameof(outcome));
            WriteOutcome(permit.Intent.AttemptId, outcome!, lease);
        }
    }

    /// <summary>
    /// A reread after an unmatched intent is diagnostic even when it matches Desired.
    /// It never becomes Applied and therefore cannot create an ordinary undo entry.
    /// </summary>
    public void RecordRecoveryObservation(Guid attemptId, RegistryValueSnapshot observed,
        string detail, IMutationLease lease)
    {
        ArgumentNullException.ThrowIfNull(observed);
        lock (_gate)
        {
            CheckLease(lease, writing: false);
            WriteOutcome(attemptId, new JournalOutcome(JournalOutcomeKind.RecoveryObservation, observed.Value, detail), lease);
        }
    }

    /// <summary>
    /// Call only after history import commits, including diagnostic outcomes.
    /// This durable acknowledgement does not delete or compact any journal file.
    /// </summary>
    public void AcknowledgeImported(Guid attemptId, string transactionId, IMutationLease lease)
    {
        lock (_gate)
        {
            CheckLease(lease, writing: true);
            CheckDirectory();
            if (string.IsNullOrWhiteSpace(transactionId) || transactionId.Length > 256)
                throw new ArgumentException("A bounded history transaction id is required.", nameof(transactionId));
            var attempt = Find(HealthySnapshot(), attemptId);
            if (attempt.Outcome is null) throw new InvalidOperationException("An unmatched intent cannot be acknowledged as imported.");
            if (attempt.ImportTransactionId is { } prior)
            {
                if (prior != transactionId) throw new InvalidOperationException("Conflicting import acknowledgement.");
                return;
            }
            Append(new JournalRecord(1, "ack", attemptId, ImportTransactionId: transactionId), lease);
        }
    }

    private void WriteOutcome(Guid attemptId, JournalOutcome outcome, IMutationLease lease)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        CheckDirectory(harden: outcome.Kind != JournalOutcomeKind.RecoveryObservation);
        var attempt = Find(HealthySnapshot(), attemptId);
        ValidateOutcome(attempt.Intent, outcome);
        if (attempt.Outcome is { } prior)
        {
            if (prior != outcome) throw new InvalidOperationException("Conflicting outcome for this attempt.");
            return;
        }
        Append(new JournalRecord(1, "outcome", attemptId, Outcome: outcome), lease);
    }

    private static JournalAttempt Find(JournalSnapshot snapshot, Guid attemptId) =>
        snapshot.Attempts.SingleOrDefault(a => a.Intent.AttemptId == attemptId)
        ?? throw new InvalidOperationException("Journal intent does not exist.");

    private JournalSnapshot HealthySnapshot()
    {
        var snapshot = ReadCore();
        if (!snapshot.IsHealthy) throw new InvalidDataException(string.Join("; ", snapshot.Faults));
        return snapshot;
    }

    private void CheckDirectory(bool harden = true)
    {
        // A caller-provided path is not evidence of trust. Reject before any repair.
        if (!Directory.Exists(_directory) || !_trustCheck(_directory))
            throw new UnauthorizedAccessException("Journal directory trust check failed.");
        // Recovery reads cannot repair DACLs. The trust predicate must verify
        // existing directory protection and ownership before recovery reads.
        if (!harden) return;
        var hardened = _guard.EnsureHardened(_directory);
        if (!hardened.IsSuccess || hardened.Value == DaclStatus.Failed)
            throw new UnauthorizedAccessException("Journal directory hardening failed.");
    }

    private static void CheckLease(IMutationLease lease, bool writing)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!lease.IsHeld || (writing && !lease.CanWrite))
            throw new InvalidOperationException("A held, recovered mutation lease is required for journal writes.");
    }

    private string AttemptPath(Guid id) => Path.Join(_directory, id.ToString("N") + ".tipj");

    private JournalSnapshot ReadCore()
    {
        var attempts = new List<JournalAttempt>();
        var faults = new List<string>();
        long bytes = 0;
        long files = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(_directory))
        {
            if (++files * AttemptReservationBytes > _maximumBytes)
            {
                faults.Add("Journal entry count exceeds its storage bound.");
                break;
            }
            if (!_trustCheck(path)) throw new UnauthorizedAccessException("Journal file trust check failed.");
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id)
                || Path.GetExtension(path) != ".tipj" || Directory.Exists(path))
            {
                faults.Add("Unexpected entry in journal directory.");
                break;
            }
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            bytes += file.Length;
            if (bytes > _maximumBytes || file.Length > AttemptReservationBytes)
            {
                faults.Add("Journal exceeds its storage bound.");
                break;
            }
            JournalAttempt? attempt = null;
            try
            {
                while (file.Position < file.Length)
                {
                    var record = ReadRecord(file);
                    if (record.AttemptId != id || record.Version != 1)
                        throw new InvalidDataException("Journal identity or version mismatch.");
                    attempt = ApplyRecord(attempt, record);
                }
                if (attempt is null) throw new InvalidDataException("Empty journal segment.");
            }
            catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or JsonException or ArgumentException)
            {
                faults.Add($"{id:N}: {ex.Message}");
            }
            if (attempt is not null) attempts.Add(attempt);
        }
        if (faults.Count > 0)
            attempts = attempts.Select(a => a with { DiagnosticOnly = true }).ToList();
        return new JournalSnapshot(attempts.AsReadOnly(), faults.AsReadOnly());
    }

    private static JournalRecord ReadRecord(FileStream file)
    {
        Span<byte> header = stackalloc byte[8];
        file.ReadExactly(header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic || length is <= 0 or > MaximumPayloadBytes)
            throw new InvalidDataException("Invalid journal frame header.");
        var payload = new byte[length];
        file.ReadExactly(payload);
        Span<byte> hash = stackalloc byte[32];
        file.ReadExactly(hash);
        if (!CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(payload)))
            throw new InvalidDataException("Journal checksum mismatch.");
        return JsonSerializer.Deserialize(payload, JournalJsonContext.Default.JournalRecord)
            ?? throw new InvalidDataException("Empty journal record.");
    }

    private static JournalAttempt ApplyRecord(JournalAttempt? attempt, JournalRecord record)
    {
        if (record.Type == "intent" && attempt is null && record.Intent is { } intent
            && record.Outcome is null && record.ImportTransactionId is null)
        {
            ValidateIntent(intent);
            if (intent.AttemptId != record.AttemptId) throw new InvalidDataException("Intent identity mismatch.");
            return new JournalAttempt(intent, null, null);
        }
        if (record.Type == "outcome" && attempt is { Outcome: null } && record.Outcome is { } outcome
            && record.Intent is null && record.ImportTransactionId is null)
        {
            ValidateOutcome(attempt.Intent, outcome);
            return attempt with { Outcome = outcome };
        }
        if (record.Type == "ack" && attempt is { Outcome: not null, ImportTransactionId: null }
            && record.Intent is null && record.Outcome is null
            && !string.IsNullOrWhiteSpace(record.ImportTransactionId) && record.ImportTransactionId.Length <= 256)
            return attempt with { ImportTransactionId = record.ImportTransactionId };
        throw new InvalidDataException("Invalid journal record order or fields.");
    }

    private static void ValidateIntent(JournalIntent intent)
    {
        if (intent.AttemptId == Guid.Empty || intent.LeaseId == Guid.Empty
            || intent.CreatedAt == default || intent.Observed is null || intent.Desired is null)
            throw new InvalidDataException("Incomplete journal intent.");
        var validation = RestorationCatalog.Default.Validate(new DriftBaselineEntry
        {
            ModuleId = intent.ModuleId, SettingId = intent.SettingId, DisplayName = intent.SettingId,
            SystemLocation = intent.CanonicalLocation, ValueType = Changes.ChangeValueType.Registry_DWord,
            ExpectedValue = intent.Desired.Data, UpdatedAtUtc = intent.CreatedAt,
        }, intent.UserSid);
        if (!RegistryValueSnapshot.TryCanonicalize(intent.Observed, out var canonicalObserved) || canonicalObserved != intent.Observed
            || !validation.IsAccepted || validation.Candidate!.DesiredValue != intent.Desired
            || !RestorationBatchFactory.Prepare(validation.Candidate, RegistryValueSnapshot.Present(intent.Observed)).IsReady)
            throw new InvalidDataException("Journal intent does not match the restoration catalog.");
    }

    private static void ValidateOutcome(JournalIntent intent, JournalOutcome outcome)
    {
        if (!Enum.IsDefined(outcome.Kind) || outcome.Detail is null || outcome.Detail.Length > 2048
            || (outcome.Observed is { } observed
                && (!RegistryValueSnapshot.TryCanonicalize(observed, out var canonical) || canonical != observed))
            || (outcome.Kind == JournalOutcomeKind.Applied && outcome.Observed != intent.Desired))
            throw new InvalidDataException("Invalid journal outcome.");
    }

    private void Append(JournalRecord record, IMutationLease lease, bool create = false)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(record, JournalJsonContext.Default.JournalRecord);
        if (payload.Length > MaximumPayloadBytes) throw new InvalidDataException("Journal record exceeds the frame bound.");
        var path = AttemptPath(record.AttemptId);
        if (!create && !_trustCheck(path)) throw new UnauthorizedAccessException("Journal file trust check failed.");
        // Recovery evidence is the sole append allowed before MarkRecovered.
        // It cannot create an intent, Applied result, acknowledgement, or permit.
        var recoveryEvidence = !create && record.Type == "outcome"
            && record.Outcome?.Kind == JournalOutcomeKind.RecoveryObservation;
        CheckLease(lease, writing: !recoveryEvidence);
        using var file = new FileStream(path, create ? FileMode.CreateNew : FileMode.Open,
            FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        if (!_trustCheck(path)) throw new UnauthorizedAccessException("Created journal file failed its trust check.");
        file.Position = file.Length;
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], payload.Length);
        file.Write(header);
        file.Write(payload);
        file.Write(SHA256.HashData(payload));
        file.Flush(flushToDisk: true);
    }
}
