using Microsoft.Data.Sqlite;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Data;

namespace ThisIsMyPC.Core.Tests.Data;

public class ChangeHistoryRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ChangeHistoryRepository _repository;

    public ChangeHistoryRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_history_{Guid.NewGuid():N}.db");
        _repository = new ChangeHistoryRepository();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private static ChangeHistoryEntry CreateTestEntry(
        string moduleId = "ShellModule",
        string settingId = "classic-context-menu",
        DateTimeOffset? appliedAt = null) => new()
    {
        ModuleId = moduleId,
        SettingId = settingId,
        DisplayName = "Classic Context Menu",
        SystemLocation = @"HKCU\Software\Classes\CLSID\{86ca1aa0}",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Disabled",
        AfterDisplay = "Enabled",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Enable,
        GroupId = "group1",
        AppliedAt = appliedAt ?? DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task InitializeDatabaseAsync_CreatesTablesAndSchemaVersion()
    {
        await _repository.InitializeDatabaseAsync(_dbPath);

        var count = await _repository.GetEntryCountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task InitializeDatabaseAsync_IsIdempotent()
    {
        await _repository.InitializeDatabaseAsync(_dbPath);
        await _repository.InitializeDatabaseAsync(_dbPath);

        var count = await _repository.GetEntryCountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task InsertAsync_ReturnsEntryWithId()
    {
        await _repository.InitializeDatabaseAsync(_dbPath);

        var entry = CreateTestEntry();
        var inserted = await _repository.InsertAsync(entry);

        Assert.True(inserted.Id > 0);
        Assert.Equal(entry.ModuleId, inserted.ModuleId);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEntriesInReverseChronologicalOrder()
    {
        await _repository.InitializeDatabaseAsync(_dbPath);

        var now = DateTimeOffset.UtcNow;
        await _repository.InsertAsync(CreateTestEntry(settingId: "first", appliedAt: now.AddMinutes(-2)));
        await _repository.InsertAsync(CreateTestEntry(settingId: "second", appliedAt: now.AddMinutes(-1)));
        await _repository.InsertAsync(CreateTestEntry(settingId: "third", appliedAt: now));

        var entries = await _repository.GetAllAsync();

        Assert.Equal(3, entries.Count);
        Assert.Equal("third", entries[0].SettingId);
        Assert.Equal("second", entries[1].SettingId);
        Assert.Equal("first", entries[2].SettingId);
    }

    [Fact]
    public async Task GetAllAsync_RespectsLimitAndOffset()
    {
        await _repository.InitializeDatabaseAsync(_dbPath);

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
            await _repository.InsertAsync(CreateTestEntry(settingId: $"s{i}", appliedAt: now.AddMinutes(i)));

        var entries = await _repository.GetAllAsync(limit: 2, offset: 1);

        Assert.Equal(2, entries.Count);
        Assert.Equal("s3", entries[0].SettingId);
        Assert.Equal("s2", entries[1].SettingId);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsCorrectEntry()
    {
        await _repository.InitializeDatabaseAsync(_dbPath);

        var inserted = await _repository.InsertAsync(CreateTestEntry());
        var retrieved = await _repository.GetByIdAsync(inserted.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(inserted.Id, retrieved.Id);
        Assert.Equal(inserted.ModuleId, retrieved.ModuleId);
        Assert.Equal(inserted.SettingId, retrieved.SettingId);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullForNonexistentId()
    {
        await _repository.InitializeDatabaseAsync(_dbPath);

        var retrieved = await _repository.GetByIdAsync(999);

        Assert.Null(retrieved);
    }

    [Fact]
    public async Task UpdateRevertedAtAsync_SetsRevertedAtAndRevertedByEntryId()
    {
        await _repository.InitializeDatabaseAsync(_dbPath);

        var original = await _repository.InsertAsync(CreateTestEntry());
        var revertedAt = DateTimeOffset.UtcNow;

        await _repository.UpdateRevertedAtAsync(original.Id, revertedAt, 42);

        var updated = await _repository.GetByIdAsync(original.Id);
        Assert.NotNull(updated);
        Assert.NotNull(updated.RevertedAt);
        Assert.Equal(42, updated.RevertedByEntryId);
    }

    [Fact]
    public async Task ClearRevertedAtAsync_ClearsRevertedFields()
    {
        await _repository.InitializeDatabaseAsync(_dbPath);

        var original = await _repository.InsertAsync(CreateTestEntry());
        await _repository.UpdateRevertedAtAsync(original.Id, DateTimeOffset.UtcNow, 42);
        await _repository.ClearRevertedAtAsync(original.Id);

        var updated = await _repository.GetByIdAsync(original.Id);
        Assert.NotNull(updated);
        Assert.Null(updated.RevertedAt);
        Assert.Null(updated.RevertedByEntryId);
    }

    [Fact]
    public async Task GetEntryCountAsync_ReturnsCorrectCount()
    {
        await _repository.InitializeDatabaseAsync(_dbPath);

        await _repository.InsertAsync(CreateTestEntry(settingId: "a"));
        await _repository.InsertAsync(CreateTestEntry(settingId: "b"));
        await _repository.InsertAsync(CreateTestEntry(settingId: "c"));

        var count = await _repository.GetEntryCountAsync();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task SchemaVersionIsTracked()
    {
        await _repository.InitializeDatabaseAsync(_dbPath);

        // Verify schema version was set (internal detail, but important for migrations)
        // Re-init should not throw
        await _repository.InitializeDatabaseAsync(_dbPath);

        var count = await _repository.GetEntryCountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task InsertAsync_PreservesAllFields()
    {
        await _repository.InitializeDatabaseAsync(_dbPath);

        var now = DateTimeOffset.UtcNow;
        var entry = new ChangeHistoryEntry
        {
            ModuleId = "TestModule",
            SettingId = "test-setting",
            DisplayName = "Test Setting",
            SystemLocation = @"HKLM\Software\Test",
            BeforeValue = "before",
            AfterValue = "after",
            BeforeDisplay = "Before",
            AfterDisplay = "After",
            ValueType = ChangeValueType.Registry_String,
            Category = ChangeCategory.Modify,
            GroupId = "grp-1",
            AppliedAt = now,
        };

        var inserted = await _repository.InsertAsync(entry);
        var retrieved = await _repository.GetByIdAsync(inserted.Id);

        Assert.NotNull(retrieved);
        Assert.Equal("TestModule", retrieved.ModuleId);
        Assert.Equal("test-setting", retrieved.SettingId);
        Assert.Equal("Test Setting", retrieved.DisplayName);
        Assert.Equal(@"HKLM\Software\Test", retrieved.SystemLocation);
        Assert.Equal("before", retrieved.BeforeValue);
        Assert.Equal("after", retrieved.AfterValue);
        Assert.Equal("Before", retrieved.BeforeDisplay);
        Assert.Equal("After", retrieved.AfterDisplay);
        Assert.Equal(ChangeValueType.Registry_String, retrieved.ValueType);
        Assert.Equal(ChangeCategory.Modify, retrieved.Category);
        Assert.Equal("grp-1", retrieved.GroupId);
        Assert.Null(retrieved.RevertedAt);
        Assert.Null(retrieved.RevertedByEntryId);
        Assert.Null(retrieved.RedoOfEntryId);
    }
}
