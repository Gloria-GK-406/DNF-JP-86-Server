using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Accounts;

public sealed class AuthenticationSchemaTests
{
    [Fact]
    public async Task UpgradingAnExistingAccountDatabaseIsIdempotentAndKeepsItsAccount()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-auth-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "accounts.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE accounts(account_id INTEGER PRIMARY KEY AUTOINCREMENT,m_id TEXT NOT NULL UNIQUE,password_hash TEXT NOT NULL DEFAULT '',created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP); INSERT INTO accounts(account_id,m_id,password_hash) VALUES(41,'legacy','old-value'); CREATE TABLE login_tickets(ticket_digest TEXT PRIMARY KEY,account_id INTEGER,issued_at INTEGER,expires_at INTEGER,consumed_at INTEGER); INSERT INTO login_tickets VALUES('obsolete',41,1,2,NULL);";
                await command.ExecuteNonQueryAsync();
            }
            HandWrittenShape.Adopt(path);
            HandWrittenShape.Adopt(path);
            var original = await new AccountSide(path).GetOrCreateAsync("legacy", "ignored");
            var reopened = await new AccountSide(path).GetOrCreateAsync("legacy", "ignored-again");
            Assert.Equal(41, reopened.AccountId.Value);
            Assert.Equal(original, reopened);
            Assert.Equal("old-value", reopened.PasswordHash);
            await using var check = new SqliteConnection($"Data Source={path};Pooling=False");
            await check.OpenAsync();
            using var query = check.CreateCommand();
            query.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='login_tickets'";
            Assert.Equal(0L, await query.ExecuteScalarAsync());
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }
}
