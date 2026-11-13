using Microsoft.Data.Sqlite;

namespace DbMigrate;

/// <summary>
/// Drops <c>characters.manage_level</c> (schema 4 to 5).
/// </summary>
/// <remarks>
/// The adventure group (冒险团) belongs to the account and is worked out from its characters' levels
/// whenever it is asked for; nothing ever wrote this per-character column, so every row holds zero
/// and nothing is lost by dropping it. A database already without the column -- one built from the
/// current shape and stamped back for a test -- is left as it is.
/// </remarks>
internal static class AdventureGroupColumnRetirement
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        if (Shape.HasColumn(connection, transaction, "characters", "manage_level"))
        {
            using var drop = connection.CreateCommand();
            drop.Transaction = transaction;
            drop.CommandText = "ALTER TABLE characters DROP COLUMN manage_level;";
            drop.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
