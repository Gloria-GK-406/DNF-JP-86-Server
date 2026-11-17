using Microsoft.Data.Sqlite;

namespace DbMigrate;

internal static class GameplayParameterStorageRetirement
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // These are derived effects, never replacements for event_configuration or fatigue ledgers.
        command.CommandText = "DROP TABLE IF EXISTS fatigue_consumption_settings; DROP TABLE IF EXISTS drop_draw_multiplier;";
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
