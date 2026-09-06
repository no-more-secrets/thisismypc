using System.Text.Json.Serialization;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Drift.Journal;

public enum JournalOutcomeKind { Applied, Failed, Uncertain, RecoveryObservation }

/// <summary>Canonical catalog identity and profile remain separate from the SYSTEM write path.</summary>
public sealed record JournalIntent(Guid AttemptId, Guid LeaseId, DateTimeOffset CreatedAt,
    string ModuleId, string SettingId, string CanonicalLocation, string UserSid,
    RegistryValueData Observed, RegistryValueData Desired);

/// <summary>Null Observed means absent; it is never an empty-string sentinel.</summary>
public sealed record JournalOutcome(JournalOutcomeKind Kind, RegistryValueData? Observed, string Detail);

public sealed record JournalAttempt(JournalIntent Intent, JournalOutcome? Outcome, string? ImportTransactionId,
    bool DiagnosticOnly = false)
{
    public bool IsUncertain => Outcome is null || Outcome.Kind != JournalOutcomeKind.Applied;
    public bool CanImportOrdinaryUndo => !DiagnosticOnly && !IsUncertain && ImportTransactionId is null;
}

/// <summary>Any fault blocks writes and history import. Prefix entries are diagnostic only in that case.</summary>
public sealed record JournalSnapshot(IReadOnlyList<JournalAttempt> Attempts, IReadOnlyList<string> Faults)
{
    public bool IsHealthy => Faults.Count == 0;
}

/// <summary>Issued only after a new intent is flushed. A duplicate attempt never grants permission to repeat its write.</summary>
public sealed class DurableIntent
{
    internal DurableIntent(object issuer, JournalIntent intent) { Issuer = issuer; Intent = intent; }
    internal object Issuer { get; }
    public JournalIntent Intent { get; }
}

public sealed record BeginJournalAttempt(bool AlreadyExists, DurableIntent? Permit);

internal sealed record JournalRecord(int Version, string Type, Guid AttemptId,
    JournalIntent? Intent = null, JournalOutcome? Outcome = null, string? ImportTransactionId = null);

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(JournalRecord))]
internal sealed partial class JournalJsonContext : JsonSerializerContext;
