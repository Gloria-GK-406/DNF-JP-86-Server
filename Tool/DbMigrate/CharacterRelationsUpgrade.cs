using Microsoft.Data.Sqlite;

namespace DbMigrate;

/// <summary>Adds the table that keeps friends and blacklists between characters (schema 4 to 5).</summary>
/// <remarks>
/// The statements are copied from the version-5 declaration rather than read from it: this step
/// must keep producing the version-5 shape even after the declaration moves on, and the next shape
/// is the next step's business. No existing row is read or changed.
/// </remarks>
internal static class CharacterRelationsUpgrade
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS character_relations(owner_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE CASCADE,target_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE CASCADE,kind INTEGER NOT NULL CHECK(kind BETWEEN 1 AND 255),created_at INTEGER NOT NULL,PRIMARY KEY(owner_id,kind,target_id),CHECK(owner_id<>target_id));
            CREATE INDEX IF NOT EXISTS ix_character_relations_target ON character_relations(target_id,kind);
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
