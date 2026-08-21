using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Accounts;

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
            var created = await new AccountSide(databasePath)
                .GetOrCreateAsync("10038", "first-hash");

            // A second one over the same file, so that reuse is decided by what is stored rather
            // than by anything the first left in memory.
            var reused = await new AccountSide(databasePath)
                .GetOrCreateAsync("10038", "replacement-hash");

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
