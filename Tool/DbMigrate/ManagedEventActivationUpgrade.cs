using Microsoft.Data.Sqlite;

namespace DbMigrate;

internal static class ManagedEventActivationUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Legacy 0->1 may already have created these current declarations.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS event_activation_state(event_id INTEGER PRIMARY KEY,active INTEGER NOT NULL CHECK(active IN(0,1)),configuration_revision INTEGER NOT NULL,applied_at INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS fatigue_consumption_settings(id INTEGER PRIMARY KEY CHECK(id=1),suspended INTEGER NOT NULL CHECK(suspended IN(0,1)));
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
