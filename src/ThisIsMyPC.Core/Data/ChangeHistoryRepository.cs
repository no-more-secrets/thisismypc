using System.Globalization;
using Microsoft.Data.Sqlite;
using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Core.Data;

public sealed class ChangeHistoryRepository
{
    private const int CurrentSchemaVersion = 1;

    private const string CreateSchemaV1 = """
        CREATE TABLE IF NOT EXISTS change_history (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          module_id TEXT NOT NULL,
          setting_id TEXT NOT NULL,
          display_name TEXT NOT NULL,
          system_location TEXT NOT NULL,
          before_value TEXT,
          after_value TEXT,
          before_display TEXT,
          after_display TEXT,
          value_type TEXT NOT NULL,
          category TEXT NOT NULL,
          group_id TEXT,
          applied_at TEXT NOT NULL,
          reverted_at TEXT,
          reverted_by_entry_id INTEGER,
          redo_of_entry_id INTEGER
        );
        CREATE TABLE IF NOT EXISTS schema_version (
          version INTEGER PRIMARY KEY,
          applied_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_change_history_applied_at ON change_history(applied_at DESC);
        CREATE INDEX IF NOT EXISTS ix_change_history_module_id ON change_history(module_id);
        CREATE INDEX IF NOT EXISTS ix_change_history_group_id ON change_history(group_id);
        """;

    private string? _connectionString;

    public async Task InitializeDatabaseAsync(string dbPath)
    {
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var schemaVersion = await GetSchemaVersionAsync(connection).ConfigureAwait(false);

        if (schemaVersion < CurrentSchemaVersion)
        {
            await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

            if (schemaVersion < 1)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = CreateSchemaV1;
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await SetSchemaVersionAsync(connection, CurrentSchemaVersion).ConfigureAwait(false);
            await transaction.CommitAsync().ConfigureAwait(false);
        }
    }

    public async Task<ChangeHistoryEntry> InsertAsync(ChangeHistoryEntry entry)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            INSERT INTO change_history (
                module_id, setting_id, display_name, system_location,
                before_value, after_value, before_display, after_display,
                value_type, category, group_id, applied_at,
                reverted_at, reverted_by_entry_id, redo_of_entry_id
            ) VALUES (
                @module_id, @setting_id, @display_name, @system_location,
                @before_value, @after_value, @before_display, @after_display,
                @value_type, @category, @group_id, @applied_at,
                @reverted_at, @reverted_by_entry_id, @redo_of_entry_id
            );
            SELECT last_insert_rowid();
            """;

        AddEntryParameters(cmd, entry);

        var id = (long)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;
        return entry with { Id = id };
    }

    public async Task<IReadOnlyList<ChangeHistoryEntry>> GetAllAsync(int? limit = null, int? offset = null)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        cmd.CommandText = "SELECT * FROM change_history ORDER BY applied_at DESC";

        if (limit.HasValue)
        {
            cmd.CommandText += " LIMIT @limit";
            cmd.Parameters.AddWithValue("@limit", limit.Value);
        }

        if (offset.HasValue)
        {
            if (!limit.HasValue)
            {
                cmd.CommandText += " LIMIT -1";
            }
            cmd.CommandText += " OFFSET @offset";
            cmd.Parameters.AddWithValue("@offset", offset.Value);
        }

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        var entries = new List<ChangeHistoryEntry>();

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    public async Task<ChangeHistoryEntry?> GetByIdAsync(long id)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        cmd.CommandText = "SELECT * FROM change_history WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        return await reader.ReadAsync().ConfigureAwait(false) ? ReadEntry(reader) : null;
    }

    public async Task UpdateRevertedAtAsync(long id, DateTimeOffset revertedAt, long revertedByEntryId)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            UPDATE change_history
            SET reverted_at = @reverted_at, reverted_by_entry_id = @reverted_by_entry_id
            WHERE id = @id
            """;

        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@reverted_at", revertedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@reverted_by_entry_id", revertedByEntryId);

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task ClearRevertedAtAsync(long id)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            UPDATE change_history
            SET reverted_at = NULL, reverted_by_entry_id = NULL
            WHERE id = @id
            """;

        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task InsertBatchAsync(IReadOnlyList<ChangeHistoryEntry> entries)
    {
        if (entries.Count == 0)
            return;

        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        foreach (var entry in entries)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO change_history (
                    module_id, setting_id, display_name, system_location,
                    before_value, after_value, before_display, after_display,
                    value_type, category, group_id, applied_at,
                    reverted_at, reverted_by_entry_id, redo_of_entry_id
                ) VALUES (
                    @module_id, @setting_id, @display_name, @system_location,
                    @before_value, @after_value, @before_display, @after_display,
                    @value_type, @category, @group_id, @applied_at,
                    @reverted_at, @reverted_by_entry_id, @redo_of_entry_id
                )
                """;
            AddEntryParameters(cmd, entry);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    public async Task<int> GetEntryCountAsync()
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        cmd.CommandText = "SELECT COUNT(*) FROM change_history";
        var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    public async Task<int> GetGroupCountAsync()
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        cmd.CommandText = "SELECT COUNT(DISTINCT group_id) FROM change_history WHERE group_id IS NOT NULL";
        var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<ChangeHistoryEntry>> GetRecentGroupedAsync(int groupLimit)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            SELECT * FROM change_history
            WHERE group_id IN (
                SELECT group_id FROM change_history
                WHERE group_id IS NOT NULL
                GROUP BY group_id
                ORDER BY MAX(applied_at) DESC
                LIMIT @group_limit
            )
            ORDER BY applied_at DESC
            """;
        cmd.Parameters.AddWithValue("@group_limit", groupLimit);

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        var entries = new List<ChangeHistoryEntry>();

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    public async Task DeleteAllAsync()
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        cmd.CommandText = "DELETE FROM change_history";
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        if (_connectionString is null)
            throw new InvalidOperationException("Database not initialized. Call InitializeDatabaseAsync first.");

        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection connection)
    {
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT MAX(version) FROM schema_version";
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return result is DBNull or null ? 0 : Convert.ToInt32(result);
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    private static async Task SetSchemaVersionAsync(SqliteConnection connection, int version)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO schema_version (version, applied_at)
            VALUES (@version, @applied_at)
            """;
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@applied_at", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static void AddEntryParameters(SqliteCommand cmd, ChangeHistoryEntry entry)
    {
        cmd.Parameters.AddWithValue("@module_id", entry.ModuleId);
        cmd.Parameters.AddWithValue("@setting_id", entry.SettingId);
        cmd.Parameters.AddWithValue("@display_name", entry.DisplayName);
        cmd.Parameters.AddWithValue("@system_location", entry.SystemLocation);
        cmd.Parameters.AddWithValue("@before_value", (object?)entry.BeforeValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@after_value", (object?)entry.AfterValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@before_display", (object?)entry.BeforeDisplay ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@after_display", (object?)entry.AfterDisplay ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@value_type", entry.ValueType.ToString());
        cmd.Parameters.AddWithValue("@category", entry.Category.ToString());
        cmd.Parameters.AddWithValue("@group_id", (object?)entry.GroupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@applied_at", entry.AppliedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@reverted_at", entry.RevertedAt.HasValue ? entry.RevertedAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@reverted_by_entry_id", entry.RevertedByEntryId.HasValue ? entry.RevertedByEntryId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@redo_of_entry_id", entry.RedoOfEntryId.HasValue ? entry.RedoOfEntryId.Value : DBNull.Value);
    }

    private static ChangeHistoryEntry ReadEntry(SqliteDataReader reader)
    {
        var revertedAtStr = reader["reverted_at"] as string;
        var revertedByRaw = reader["reverted_by_entry_id"];
        var redoOfRaw = reader["redo_of_entry_id"];

        return new ChangeHistoryEntry
        {
            Id = (long)reader["id"],
            ModuleId = (string)reader["module_id"],
            SettingId = (string)reader["setting_id"],
            DisplayName = (string)reader["display_name"],
            SystemLocation = (string)reader["system_location"],
            BeforeValue = reader["before_value"] as string,
            AfterValue = reader["after_value"] as string,
            BeforeDisplay = reader["before_display"] as string,
            AfterDisplay = reader["after_display"] as string,
            ValueType = Enum.Parse<ChangeValueType>((string)reader["value_type"]),
            Category = Enum.Parse<ChangeCategory>((string)reader["category"]),
            GroupId = reader["group_id"] as string,
            AppliedAt = DateTimeOffset.Parse((string)reader["applied_at"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            RevertedAt = revertedAtStr is not null ? DateTimeOffset.Parse(revertedAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : null,
            RevertedByEntryId = revertedByRaw is long rbei ? rbei : null,
            RedoOfEntryId = redoOfRaw is long roei ? roei : null,
        };
    }
}
