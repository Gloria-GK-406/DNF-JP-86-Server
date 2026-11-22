using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

public sealed class AntonRaidRewardMigrationTests
{
    [Fact]
    public void VersionTwelveCreatesLedgersAndKeepsValidStageCounts()
    {
        var path = Unversioned.NewPath("anton-reward-v12");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                DROP TABLE anton_raid_reward_ledger;
                DROP TABLE anton_raid_quest_periods;
                INSERT INTO character_active_quests(character_id, slot, quest_id, trigger_value)
                    VALUES (1, 0, 12830, 1), (2, 0, 12830, 1027), (3, 0, 12830, 6);
                INSERT INTO character_achievement_complete(character_id, sort_order, achievement_id, p1, p2, p3, p4)
                    VALUES (1, 0, 6742, 1, 0, 0, 0), (2, 0, 6743, 0, 0, 0, 0);
                PRAGMA user_version=12;
                """);

            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal(12, result.From);
            Assert.Equal(13, result.To);
            Assert.Equal(2L, Scalar(connection, """
                SELECT COUNT(*) FROM sqlite_master WHERE name IN ('anton_raid_reward_ledger', 'anton_raid_quest_periods');
                """));
            Assert.Equal(2565L, Scalar(connection, "SELECT trigger_value FROM character_active_quests WHERE character_id=1;"));
            Assert.Equal(1027L, Scalar(connection, "SELECT trigger_value FROM character_active_quests WHERE character_id=2;"));
            Assert.Equal(2565L, Scalar(connection, "SELECT trigger_value FROM character_active_quests WHERE character_id=3;"));
            Assert.Equal(15L, Scalar(connection, "SELECT p1 FROM character_achievement_complete WHERE character_id=1;"));
            Assert.Equal(0L, Scalar(connection, "SELECT p1 FROM character_achievement_complete WHERE character_id=2;"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
