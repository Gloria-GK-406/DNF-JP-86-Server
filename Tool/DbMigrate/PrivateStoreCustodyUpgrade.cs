using Microsoft.Data.Sqlite;

namespace DbMigrate;

/// <summary>Adds private store custody: the goods laid out on stalls (schema 7 to 8).</summary>
/// <remarks>Copied from the version-8 declaration so this step keeps its shape. No existing row is touched.</remarks>
internal static class PrivateStoreCustodyUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS private_store_custody(owner_id INTEGER NOT NULL,display_slot INTEGER NOT NULL CHECK(display_slot BETWEEN 0 AND 13),unit_price INTEGER NOT NULL CHECK(unit_price>0),kind TEXT NOT NULL CHECK(kind IN('equipment','stackable')),item_uid INTEGER NOT NULL DEFAULT 0,item_id INTEGER NOT NULL,item_count INTEGER NOT NULL CHECK(item_count>0),item_core BLOB,PRIMARY KEY(owner_id,display_slot),CHECK(kind='stackable' OR (item_uid>0 AND item_count=1)),CHECK(kind='equipment' OR (item_uid=0 AND item_core IS NOT NULL)));
            CREATE UNIQUE INDEX IF NOT EXISTS ux_private_store_custody_equipment ON private_store_custody(item_uid) WHERE kind='equipment';
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
