using Microsoft.Data.Sqlite;

namespace DbMigrate;

/// <summary>
/// Adds <c>characters.aura_skin_slot</c> (schema 8 to 9).
/// </summary>
/// <remarks>
/// Whether the character has opened its aura transmog avatar slot (光环幻化栏) with the 490700411
/// ticket. Nobody has opened it before this column, so every existing character starts closed. A
/// database that already has the column is left as it is.
/// </remarks>
internal static class AuraSkinSlotUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        if (!Shape.HasColumn(connection, transaction, "characters", "aura_skin_slot"))
        {
            using var add = connection.CreateCommand();
            add.Transaction = transaction;
            add.CommandText = "ALTER TABLE characters ADD COLUMN aura_skin_slot INTEGER NOT NULL DEFAULT 0;";
            add.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
