using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

public sealed class SchemaVersionTests
{
    /// <summary>
    /// An unversioned database with what the old server left in it comes out of the tool in
    /// exactly the shape the server builds a new one in.
    /// </summary>
    /// <remarks>
    /// The server only checks the version, so a migration that stamped the version without
    /// reaching the shape would be accepted and fail later on whatever statement met the
    /// difference. This is what holds the two to the same thing.
    /// </remarks>
    [Fact]
    public void A_migrated_database_has_exactly_the_shape_the_server_builds()
    {
        var fresh = Unversioned.NewPath("fresh");
        var migrated = Unversioned.NewPath("migrated");
        try
        {
            _ = Unversioned.Open(fresh);
            Unversioned.CreateUnstamped(migrated);
            Execute(migrated, """
                CREATE TABLE login_tickets(ticket TEXT PRIMARY KEY);
                CREATE TABLE schema_migrations(name TEXT PRIMARY KEY, applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
                CREATE TABLE marriage_housing_migrations(version INTEGER PRIMARY KEY, applied_at INTEGER NOT NULL);
                CREATE UNIQUE INDEX idx_item_slots_account_slot ON item_slots(owner_id,slot_index) WHERE owner_scope='account';
                DROP INDEX idx_mailbox_pending_equipment_uid;
                ALTER TABLE mailbox_attachments DROP COLUMN asset_kind;
                ALTER TABLE mailbox_messages ADD COLUMN gold INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE mailbox_recipients ADD COLUMN received_gold_flag INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE characters ADD COLUMN skill_tree_index INTEGER NOT NULL DEFAULT -1;
                ALTER TABLE characters ADD COLUMN appearance_blob BLOB;
                ALTER TABLE accounts ADD COLUMN cube_black INTEGER NOT NULL DEFAULT 0;
                """);

            Unversioned.Migrate(migrated);

            Assert.Equal(ShapeOf(fresh), ShapeOf(migrated));
            Assert.Equal(CharacterDatabaseSchema.Version, Scalar(migrated, "PRAGMA user_version"));
            _ = Unversioned.Open(migrated);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(fresh);
            File.Delete(migrated);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void The_server_refuses_a_database_in_another_version(int version)
    {
        var path = Unversioned.NewPath("refused");
        try
        {
            Unversioned.CreateUnstamped(path);
            Execute(path, $"PRAGMA user_version={version};");

            var refusal = Assert.Throws<InvalidOperationException>(() => Unversioned.Open(path));
            Assert.Contains("DbMigrate", refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public void The_database_is_backed_up_before_anything_changes_and_a_current_one_is_left_alone()
    {
        var path = Unversioned.NewPath("backup");
        string? backup = null;
        try
        {
            Unversioned.CreateUnstamped(path);
            Execute(path, "INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');");

            var first = Migrator.Migrate(path);
            backup = first.Backup;

            Assert.Equal((0L, (long)CharacterDatabaseSchema.Version), (first.From, first.To));
            Assert.True(File.Exists(backup));
            Assert.Equal(0L, Scalar(backup!, "PRAGMA user_version"));
            Assert.Equal(1L, Scalar(backup!, "SELECT COUNT(*) FROM accounts WHERE m_id='kept'"));

            var again = Migrator.Migrate(path);
            Assert.Null(again.Backup);
            Assert.Equal(again.From, again.To);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            if (backup is not null) File.Delete(backup);
        }
    }

    /// <summary>Every table's columns, every index and every trigger, in a fixed order.</summary>
    private static string[] ShapeOf(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        var shape = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.name||'.'||c.name||':'||c.type||':'||c."notnull"||':'||IFNULL(c.dflt_value,'')||':'||c.pk
            FROM sqlite_master m JOIN pragma_table_info(m.name) c
            WHERE m.type='table' AND m.name NOT LIKE 'sqlite_%'
            UNION ALL
            SELECT m.type||' '||m.name||' on '||m.tbl_name||': '||IFNULL(m.sql,'')
            FROM sqlite_master m WHERE m.type IN ('index','trigger')
            ORDER BY 1;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read()) shape.Add(reader.GetString(0));
        return [.. shape];
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
