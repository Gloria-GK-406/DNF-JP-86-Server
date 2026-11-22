using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate;

internal static class AntonRaidRewardUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        CharacterDatabaseSchema.DeclareAntonRaidRewards(connection, transaction);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // v12 initialized a client counter to 1. Preserve structurally valid two-stage values,
        // handed-in quests and previously unlocked titles.
        command.CommandText = """
            UPDATE character_active_quests SET trigger_value = 2565
                WHERE quest_id = 12830 AND
                    (trigger_value = 1 OR (trigger_value & 511) > 5
                        OR ((trigger_value >> 9) & 511) > 5 OR (trigger_value >> 18) <> 0);
            UPDATE character_active_quests SET trigger_value = 5 WHERE quest_id IN (8514, 8515)
                AND (trigger_value = 1 OR trigger_value < 0 OR trigger_value > 5);
            UPDATE character_achievement_complete SET p1 = 15
                WHERE achievement_id IN (6742, 6743, 6744) AND p1 = 1 AND p2 = 0 AND p3 = 0 AND p4 = 0;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
