using System.Text.Json;
using ThisIsMyPC.Core.Drift.Journal;
using ThisIsMyPC.Core.Drift;

namespace ThisIsMyPC.Core.Data;

public sealed partial class ChangeHistoryRepository
{
    private const string MigrateToV3 = """
        ALTER TABLE change_history ADD COLUMN owner_attempt_id TEXT;
        ALTER TABLE change_history ADD COLUMN target_user_sid TEXT;
        ALTER TABLE change_history ADD COLUMN journal_outcome TEXT;
        ALTER TABLE change_history ADD COLUMN journal_detail TEXT;
        CREATE UNIQUE INDEX ix_history_owner_attempt ON change_history(owner_attempt_id) WHERE owner_attempt_id IS NOT NULL;
        CREATE TABLE owner_journal_imports (
            attempt_id TEXT PRIMARY KEY NOT NULL,
            payload_json TEXT NOT NULL,
            transaction_id TEXT NOT NULL,
            history_entry_id INTEGER NOT NULL,
            imported_at TEXT NOT NULL
        );
        """;

    // Internal: callers enter through the trusted journal reader, never an arbitrary snapshot.
    internal async Task<string> ImportJournalAttemptAsync(JournalAttempt attempt)
    {
        if (attempt.DiagnosticOnly || attempt.Outcome is null)
            throw new InvalidDataException("Only healthy terminal journal attempts can be imported.");
        var payload = JsonSerializer.Serialize(new JournalImportPayload(attempt.Intent, attempt.Outcome),
            JournalImportJsonContext.Default.JournalImportPayload);
        var id = attempt.Intent.AttemptId.ToString("N");
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using (var durability = connection.CreateCommand())
        {
            durability.CommandText = "PRAGMA synchronous=FULL";
            await durability.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        using var transaction = connection.BeginTransaction();
        await using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = "SELECT payload_json, transaction_id FROM owner_journal_imports WHERE attempt_id=@id";
        lookup.Parameters.AddWithValue("@id", id);
        string? existing = null;
        await using (var reader = await lookup.ExecuteReaderAsync().ConfigureAwait(false))
        {
            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                if (reader.GetString(0) != payload) throw new InvalidDataException("Attempt ID conflicts with imported evidence.");
                existing = reader.GetString(1);
            }
        }
        if (existing is not null)
        {
            if (attempt.ImportTransactionId is not null && attempt.ImportTransactionId != existing)
                throw new InvalidDataException("Journal acknowledgement conflicts with history receipt.");
            return existing;
        }
        if (attempt.ImportTransactionId is not null)
            throw new InvalidDataException("Acknowledged journal attempt has no history receipt.");
        var receipt = Guid.NewGuid().ToString("N");
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO change_history(module_id,setting_id,display_name,system_location,before_value,after_value,
                before_display,after_display,value_type,category,group_id,applied_at,owner_attempt_id,target_user_sid,journal_outcome,journal_detail)
            VALUES(@module,@setting,@display,@location,@before,@after,@before,@after,'Registry_DWord','SystemReversion',@id,@time,@id,@sid,@outcome,@detail);
            INSERT INTO owner_journal_imports(attempt_id,payload_json,transaction_id,history_entry_id,imported_at)
            VALUES(@id,@payload,@receipt,last_insert_rowid(),@imported);
            """;
        insert.Parameters.AddWithValue("@display", RestorationCatalog.Default.Targets.Single(t => t.ModuleId == attempt.Intent.ModuleId && t.SettingId == attempt.Intent.SettingId).DisplayName);
        insert.Parameters.AddWithValue("@outcome", attempt.Outcome.Kind.ToString());
        insert.Parameters.AddWithValue("@detail", attempt.Outcome.Detail);
        insert.Parameters.AddWithValue("@module", attempt.Intent.ModuleId);
        insert.Parameters.AddWithValue("@setting", attempt.Intent.SettingId);
        insert.Parameters.AddWithValue("@location", attempt.Intent.CanonicalLocation);
        insert.Parameters.AddWithValue("@before", attempt.Intent.Observed.Data);
        insert.Parameters.AddWithValue("@after", (object?)attempt.Outcome.Observed?.Data ?? DBNull.Value);
        insert.Parameters.AddWithValue("@id", id);
        insert.Parameters.AddWithValue("@sid", attempt.Intent.UserSid);
        insert.Parameters.AddWithValue("@time", attempt.Intent.CreatedAt.ToString("O"));
        insert.Parameters.AddWithValue("@payload", payload);
        insert.Parameters.AddWithValue("@receipt", receipt);
        insert.Parameters.AddWithValue("@imported", DateTimeOffset.UtcNow.ToString("O"));
        await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        transaction.Commit();
        return receipt;
    }
}
