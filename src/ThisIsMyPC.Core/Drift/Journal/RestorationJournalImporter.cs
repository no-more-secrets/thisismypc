using System.Text.Json;
using System.Text.Json.Serialization;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Data;

namespace ThisIsMyPC.Core.Drift.Journal;

public sealed class RestorationJournalImporter(ChangeHistoryRepository repository)
{
    public async Task<int> ImportAsync(RestorationJournal journal, IMutationLease lease)
    {
        if (!lease.CanWrite) throw new InvalidOperationException("Import requires a recovered mutation lease.");
        var snapshot = journal.Read(lease);
        if (!snapshot.IsHealthy) throw new InvalidDataException("Journal faults block history import.");
        var count = 0;
        foreach (var attempt in snapshot.Attempts.Where(a => a.Outcome is not null))
        {
            if (!lease.CanWrite) throw new InvalidOperationException("Mutation lease was released.");
            var receipt = await repository.ImportJournalAttemptAsync(attempt).ConfigureAwait(false);
            journal.AcknowledgeImported(attempt.Intent.AttemptId, receipt, lease);
            count++;
        }
        return count;
    }
}

internal sealed record JournalImportPayload(JournalIntent Intent, JournalOutcome Outcome);
[JsonSerializable(typeof(JournalImportPayload))]
internal sealed partial class JournalImportJsonContext : JsonSerializerContext;
