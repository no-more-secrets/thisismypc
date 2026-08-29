using Microsoft.Data.Sqlite;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Data;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Tests.Fakes;

namespace ThisIsMyPC.Core.Tests.Services;

/// <summary>
/// History undo/redo must preserve enforcement metadata and route enforced entries
/// through the executor — a bare module revert would leave companion services/tasks/
/// GPCache untouched (e.g. the WU orchestrator's cache keeping an undone policy alive).
/// </summary>
public class ChangeHistoryEnforcementTests : IDisposable
{
    private const string GPCachePath = @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\GPCache";

    private readonly string _dbPath;
    private readonly FakeEnforcementExecutor _executor = new();
    private readonly ChangeHistoryService _service;

    public ChangeHistoryEnforcementTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_history_enf_{Guid.NewGuid():N}.db");
        _service = new ChangeHistoryService(new ChangeHistoryRepository(), _dbPath, _executor);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private static SettingEnforcement Enforcement() => new()
    {
        GPCacheEntries = [GPCachePath],
        ReversionVectors = ["Group Policy refresh"],
    };

    private static ChangeDescriptor EnforcedChange() => new()
    {
        ModuleId = "Windows Update",
        SettingId = "no-auto-reboot",
        DisplayName = "Never auto-restart while you are signed in",
        SystemLocation = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoRebootWithLoggedOnUsers",
        BeforeValue = "",
        AfterValue = "1",
        BeforeDisplay = "Not configured",
        AfterDisplay = "Configured",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Modify,
        Enforcement = Enforcement(),
    };

    private static Task<OperationResult<bool>> Succeed(ChangeDescriptor _) =>
        Task.FromResult(OperationResult<bool>.Success(true));

    private async Task<long> RecordEnforcedChangeAsync()
    {
        await _service.InitializeAsync();
        await _service.RecordChangesAsync(new MutationResult
        {
            IsSuccess = true,
            Applied = [EnforcedChange()],
            RolledBack = [],
        });
        return (await _service.GetHistoryAsync()).Single().Id;
    }

    [Fact]
    public async Task Enforcement_RoundTripsThroughTheDatabase()
    {
        await RecordEnforcedChangeAsync();

        var entry = (await _service.GetHistoryAsync()).Single();

        Assert.NotNull(entry.Enforcement);
        Assert.Equal([GPCachePath], entry.Enforcement!.GPCacheEntries);
        Assert.Equal(["Group Policy refresh"], entry.Enforcement.ReversionVectors);
    }

    [Fact]
    public async Task Revert_EnforcedEntry_RoutesThroughTheExecutorRevertPath()
    {
        var id = await RecordEnforcedChangeAsync();

        var result = await _service.RevertChangeAsync(id, Succeed);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var reverted = Assert.Single(_executor.RevertedChanges);
        Assert.Equal([GPCachePath], reverted.Enforcement!.GPCacheEntries);
        // Swapped direction: undo of "configure" deletes the value (empty AfterValue)
        Assert.Equal("1", reverted.BeforeValue);
        Assert.Equal("", reverted.AfterValue);
    }

    [Fact]
    public async Task Redo_EnforcedEntry_RoutesThroughTheExecutorExecutePath()
    {
        var id = await RecordEnforcedChangeAsync();
        await _service.RevertChangeAsync(id, Succeed);

        var result = await _service.RedoChangeAsync(id, Succeed);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var redone = Assert.Single(_executor.ExecutedChanges);
        Assert.Equal([GPCachePath], redone.Enforcement!.GPCacheEntries);
        Assert.Equal("1", redone.AfterValue);
    }

    [Fact]
    public async Task Revert_EnforcedEntry_WithoutExecutor_FailsLoudly_EntryStaysUnreverted()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_history_noexec_{Guid.NewGuid():N}.db");
        try
        {
            var service = new ChangeHistoryService(new ChangeHistoryRepository(), dbPath);
            await service.InitializeAsync();
            await service.RecordChangesAsync(new MutationResult
            {
                IsSuccess = true,
                Applied = [EnforcedChange()],
                RolledBack = [],
            });
            var id = (await service.GetHistoryAsync()).Single().Id;
            var delegateCalled = false;

            var result = await service.RevertChangeAsync(id, _ =>
            {
                delegateCalled = true;
                return Task.FromResult(OperationResult<bool>.Success(true));
            });

            // Never silently degrade to a bare revert
            Assert.False(result.IsSuccess);
            Assert.False(delegateCalled);
            Assert.Null((await service.GetHistoryAsync()).Single().RevertedAt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task UnenforcedEntry_NeverTouchesTheExecutor()
    {
        await _service.InitializeAsync();
        await _service.RecordChangesAsync(new MutationResult
        {
            IsSuccess = true,
            Applied = [EnforcedChange() with { Enforcement = null }],
            RolledBack = [],
        });
        var id = (await _service.GetHistoryAsync()).Single().Id;

        var result = await _service.RevertChangeAsync(id, Succeed);

        Assert.True(result.IsSuccess);
        Assert.Empty(_executor.RevertedChanges);
    }

    [Fact]
    public async Task V1Database_MigratesToV2_AndAcceptsEnforcedEntries()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_history_v1_{Guid.NewGuid():N}.db");
        try
        {
            // Hand-build a v1 database (no enforcement_json column).
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE change_history (
                      id INTEGER PRIMARY KEY AUTOINCREMENT,
                      module_id TEXT NOT NULL, setting_id TEXT NOT NULL,
                      display_name TEXT NOT NULL, system_location TEXT NOT NULL,
                      before_value TEXT, after_value TEXT, before_display TEXT, after_display TEXT,
                      value_type TEXT NOT NULL, category TEXT NOT NULL,
                      group_id TEXT, applied_at TEXT NOT NULL,
                      reverted_at TEXT, reverted_by_entry_id INTEGER, redo_of_entry_id INTEGER
                    );
                    CREATE TABLE schema_version (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);
                    INSERT INTO schema_version (version, applied_at) VALUES (1, '2026-01-01T00:00:00Z');
                    INSERT INTO change_history (
                        module_id, setting_id, display_name, system_location,
                        value_type, category, applied_at
                    ) VALUES ('M', 's', 'd', 'HKLM\\X\\Y', 'Registry_DWord', 'Enable', '2026-01-02T00:00:00Z');
                    """;
                await cmd.ExecuteNonQueryAsync();
            }

            var service = new ChangeHistoryService(new ChangeHistoryRepository(), dbPath, _executor);
            await service.InitializeAsync();

            // Old row reads back with null enforcement; new enforced rows persist.
            var migrated = (await service.GetHistoryAsync()).Single();
            Assert.Null(migrated.Enforcement);

            await service.RecordChangesAsync(new MutationResult
            {
                IsSuccess = true,
                Applied = [EnforcedChange()],
                RolledBack = [],
            });
            Assert.Contains(
                await service.GetHistoryAsync(),
                e => e.Enforcement?.GPCacheEntries is [GPCachePath]);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }
}
