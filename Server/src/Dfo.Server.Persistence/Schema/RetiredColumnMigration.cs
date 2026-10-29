using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Schema;

/// <summary>
/// Removes columns whose fact now lives elsewhere or that nothing reads, from a database built
/// before they were taken out of the declarations.
/// </summary>
/// <remarks>
/// The migrations that once moved these columns into their owners still read them on databases
/// that have not been through this one, so they ask <see cref="IsPresent"/> instead of assuming a
/// shape. This runs after all of them: a column is dropped only once its contents are where the
/// server reads them now. Without the drop, a database built before and one built after would keep
/// two shapes, and the old one would carry values that look authoritative but are never read.
/// </remarks>
internal static class RetiredColumnMigration
{
    private static readonly (string Table, string Column)[] Retired =
    [
        // Mail gold is its gold attachments: their amounts and their claimed flags.
        ("mailbox_messages", "gold"),
        ("mailbox_recipients", "received_gold_flag"),
        // Love points are each spouse's points in marriage_personal_points.
        ("marriage_relations", "love_points"),
        ("marriage_weddings", "love_points"),
        // Shared materials are items in the account's shared material container.
        ("accounts", "cube_black"), ("accounts", "cube_white"), ("accounts", "cube_red"),
        ("accounts", "cube_blue"), ("accounts", "cube_clear"), ("accounts", "cube_gold"),
        ("accounts", "soul_common"), ("accounts", "soul_advanced"), ("accounts", "soul_rare"),
        ("accounts", "soul_unique"), ("accounts", "soul_legendary"),
        // Skill pages are character_skill_builds and characters.active_skill_build.
        ("characters", "skill_tree_index"),
        // Nothing has decoded the creation-time appearance blob since appearance is worn items.
        ("characters", "appearance_blob"),
        // Never written; the cargo's count is the items in it.
        ("items", "cargo_character_id"),
        ("account_cargo_state", "item_count"),
    ];

    public static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        if (IsPresent(connection, transaction, "marriage_relations", "love_points"))
            Require(connection, transaction, "SELECT 1 FROM marriage_housing_migrations WHERE version=1",
                "Love points must be split into personal points before their shared column is dropped.");
        if (IsPresent(connection, transaction, "accounts", "cube_black"))
            Require(connection, transaction, "SELECT 1 FROM schema_migrations WHERE name='account-shared-material-v1'",
                "Shared material balances must be moved into their container before their columns are dropped.");
        if (IsPresent(connection, transaction, "characters", "skill_tree_index"))
            KeepLegacySkillPages(connection, transaction);

        foreach (var (table, column) in Retired)
        {
            if (!IsPresent(connection, transaction, table, column)) continue;
            using var drop = connection.CreateCommand();
            drop.Transaction = transaction;
            drop.CommandText = $"ALTER TABLE {table} DROP COLUMN {column};";
            drop.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>Whether a table that exists still carries a column.</summary>
    public static bool IsPresent(SqliteConnection connection, SqliteTransaction? transaction, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM pragma_table_info(@table) WHERE name=@column COLLATE NOCASE";
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>
    /// Writes down the skill pages a character only had through <c>skill_tree_index</c>.
    /// </summary>
    /// <remarks>
    /// Until a character's pages are first changed they have no rows: they are read as page 0, page 1
    /// and any page holding skills, with page 1 unlocked when the old index was 0 or more, and the
    /// active page falling back to that index. These rows and the active page are what the same read
    /// gave, so the column can go without a page closing or the current page moving.
    /// </remarks>
    private static void KeepLegacySkillPages(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT character_id FROM characters c
            WHERE skill_tree_index>=0
              AND NOT EXISTS (SELECT 1 FROM character_skill_builds b WHERE b.character_id=c.character_id);
            """;
        var unmaterialized = new List<long>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) unmaterialized.Add(reader.GetInt64(0));

        command.CommandText = """
            INSERT OR IGNORE INTO character_skill_builds(character_id,build_id,unlocked)
            SELECT @character, page, 1 FROM (
                SELECT 0 AS page UNION SELECT 1
                UNION SELECT page_index FROM character_skills WHERE character_id=@character AND mode='pve');
            """;
        var character = command.Parameters.Add("@character", SqliteType.Integer);
        foreach (var id in unmaterialized)
        {
            character.Value = id;
            command.ExecuteNonQuery();
        }

        command.Parameters.Clear();
        command.CommandText =
            "UPDATE characters SET active_skill_build=skill_tree_index WHERE active_skill_build IS NULL AND skill_tree_index>=0;";
        command.ExecuteNonQuery();
    }

    private static void Require(SqliteConnection connection, SqliteTransaction transaction, string query, string failure)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = query;
        if (command.ExecuteScalar() is null) throw new InvalidDataException(failure);
    }
}
