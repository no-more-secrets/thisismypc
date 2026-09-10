using Microsoft.Data.Sqlite;
using ThisIsMyPC.Core.Data;

namespace ThisIsMyPC.Core.Tests.Data;

public sealed class TrustedHistoryAccessTests
{
    [Fact]
    public async Task TrustRefusalPrecedesCreationAndEveryLaterRead()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tipc-history-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "history.db");
        var repository = new ChangeHistoryRepository();
        var permitted = false;
        var checks = 0;
        void Verify()
        {
            checks++;
            if (!permitted) throw new UnauthorizedAccessException("test refusal");
        }
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.InitializeDatabaseAsync(path, Verify));
            Assert.False(Directory.Exists(directory));
            Directory.CreateDirectory(directory);
            permitted = true;
            await repository.InitializeDatabaseAsync(path, Verify);
            var before = checks;
            Assert.Equal(0, await repository.GetEntryCountAsync());
            Assert.True(checks > before);
            Assert.True(File.Exists(path + "-journal"));
            permitted = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.GetAllAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.DeleteAllAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
