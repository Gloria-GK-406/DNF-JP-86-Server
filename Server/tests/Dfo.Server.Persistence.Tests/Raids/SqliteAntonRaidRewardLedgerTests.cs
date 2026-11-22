using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Raids;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dfo.Server.Persistence.Tests.Raids;

public sealed class SqliteAntonRaidRewardLedgerTests
{
    [Fact]
    public Task DurableClaimsAndDailyLimitsFollowTheRewardTransaction() => TestGameplay.RunAsync(async () =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"anton-raid-reward-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        try
        {
            var scope = TestRequired.TransactionScope();
            var database = new SqliteDatabase(path, scope);
            var ledger = new SqliteAntonRaidRewardLedger(database);
            var key = new AntonRaidRewardKey(Guid.NewGuid(), 0, new CharacterId(4), AntonRaidRewardKind.PartyCard);
            var period = new DateOnly(2026, 11, 19);
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using var seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO accounts(account_id, m_id) VALUES (2, 'fixture');
                    INSERT INTO characters(character_id, account_id, name) VALUES (4, 2, 'fixture');
                    CREATE TABLE test_raid_grant(qty INTEGER);
                    """;
                seed.ExecuteNonQuery();
            }

            await using (var rollback = scope.Begin())
            {
                Assert.True(await ledger.TryClaimAsync(key, default));
                Assert.True(await ledger.TryCountQuestClearAsync(key.Character, 12830, 0, period, default));
                await AddAssetAsync(database);
            }

            await using (var write = scope.Begin())
            {
                Assert.True(await ledger.TryClaimAsync(key, default));
                Assert.True(await ledger.TryCountQuestClearAsync(key.Character, 12830, 0, period, default));
                await AddAssetAsync(database);
                await write.CommitAsync(default);
            }

            var restarted = new SqliteAntonRaidRewardLedger(new SqliteDatabase(path, scope));
            await using (var write = scope.Begin())
            {
                Assert.False(await restarted.TryClaimAsync(key, default));
                Assert.True(await restarted.TryClaimAsync(key with { Phase = 1 }, default));
                Assert.True(await restarted.TryClaimAsync(key with { Kind = AntonRaidRewardKind.Gold }, default));
                Assert.False(await restarted.TryCountQuestClearAsync(key.Character, 12830, 0, period, default));
                Assert.True(await restarted.TryCountQuestClearAsync(key.Character, 12830, 1, period, default));
                Assert.True(await restarted.TryCountQuestClearAsync(key.Character, 12830, 0, period.AddDays(1), default));
            }

            using var check = new SqliteConnection(connectionString);
            check.Open();
            using var count = check.CreateCommand();
            count.CommandText = "SELECT SUM(qty) FROM test_raid_grant";
            Assert.Equal(4L, count.ExecuteScalar());
        }
        finally
        {
            using var cleanup = new SqliteConnection(connectionString);
            SqliteConnection.ClearPool(cleanup);
            File.Delete(path);
        }
    });

    private static ValueTask AddAssetAsync(SqliteDatabase database) => database.WriteAsync(async (command, ct) =>
    {
        command.CommandText = "INSERT INTO test_raid_grant VALUES (4);";
        await command.ExecuteNonQueryAsync(ct);
    }, default);
}
