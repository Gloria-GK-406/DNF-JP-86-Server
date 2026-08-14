using Dfo.Server.Infrastructure.Accounts;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Tests.Accounts;

public sealed class SqliteAccountRepositoryTests
{
    [Fact]
    public async Task GetOrCreateAsync_CreatesThenReusesAccountAcrossRepositoryInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-account-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "accounts.db");
        try
        {
            var firstRepository = new SqliteAccountRepository(databasePath);
            var created = await firstRepository.GetOrCreateAsync(
                "10038", "first-hash", CancellationToken.None);

            var secondRepository = new SqliteAccountRepository(databasePath);
            var reused = await secondRepository.GetOrCreateAsync(
                "10038", "replacement-hash", CancellationToken.None);

            Assert.Equal(created.AccountId, reused.AccountId);
            Assert.Equal("10038", reused.LoginId);
            Assert.Equal("first-hash", reused.PasswordHash);
            Assert.NotEqual(default, reused.CreatedAt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
