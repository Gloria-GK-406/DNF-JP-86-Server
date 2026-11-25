using Microsoft.Data.Sqlite;

namespace DbMigrate;

/// <summary>Gives the duel skill group its own active build (schema 14 to 15).</summary>
internal static class PvpSkillBuildUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        if (!Shape.HasColumn(connection, transaction, "character_pvp_skill_state", "active_skill_build"))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                ALTER TABLE character_pvp_skill_state
                ADD COLUMN active_skill_build INTEGER NOT NULL DEFAULT 0 CHECK (active_skill_build IN (0, 1));
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
