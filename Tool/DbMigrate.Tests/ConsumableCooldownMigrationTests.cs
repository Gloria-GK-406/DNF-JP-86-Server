using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

public sealed class ConsumableCooldownMigrationTests
{
    [Fact]
    public void Version_48_preserves_legacy_cooldown_and_usage_rows()
    {
        var path = Unversioned.NewPath("consumable-cooldowns-v48");
        try
        {
            _ = Unversioned.Open(path);
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    DROP TABLE character_consumable_cooldowns;
                    PRAGMA user_version = 48;
                    INSERT INTO accounts(account_id, m_id) VALUES (1, 'cooldown-migration');
                    INSERT INTO characters(character_id, account_id, name, job, grow_type, level, exp, slot_index, delete_flag)
                        VALUES (1, 1, 'character', 0, 0, 70, 0, 0, 0);
                    INSERT INTO character_daily_counters(character_id, counter_key, value, last_consumed_at)
                        VALUES (1, 'consumable_used_7518', 1, 1800000000000);
                    INSERT INTO character_item_cooldowns(character_id, sort_order, item_id, value)
                        VALUES (1, 1, 8474, 600);
                    """;
                command.ExecuteNonQuery();
            }
            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal((48L, (long)CharacterDatabaseSchema.Version), (result.From, result.To));

            using var reopened = new SqliteConnection($"Data Source={path}");
            reopened.Open();
            using var read = reopened.CreateCommand();
            read.CommandText = "SELECT COUNT(*) FROM character_consumable_cooldowns;";
            Assert.Equal(0L, read.ExecuteScalar());
            read.CommandText = "SELECT last_consumed_at FROM character_daily_counters WHERE counter_key = 'consumable_used_7518';";
            Assert.Equal(1800000000000L, read.ExecuteScalar());
            read.CommandText = "SELECT value FROM character_item_cooldowns WHERE item_id = 8474;";
            Assert.Equal(600L, read.ExecuteScalar());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
