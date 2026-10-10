using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Schema;

internal static class AvatarAppearanceMigration
{
    public static void Apply(SqliteConnection connection)
    {
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='items'";
        var sql = (string?)query.ExecuteScalar() ?? throw new InvalidDataException("Missing items table.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(sql, @"CHECK\s*\(\s*appearance_item_id\s*>\s*0\s*\)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return;
        // Column-only migration keeps item UIDs, referencing tables and every title override intact.
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE items RENAME COLUMN appearance_item_id TO previous_appearance_item_id;
            ALTER TABLE items ADD COLUMN appearance_item_id INTEGER CHECK(appearance_item_id >= 0);
            UPDATE items SET appearance_item_id=previous_appearance_item_id;
            ALTER TABLE items DROP COLUMN previous_appearance_item_id;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
