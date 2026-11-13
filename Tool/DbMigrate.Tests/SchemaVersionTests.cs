using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

public sealed class SchemaVersionTests
{
    [Fact]
    public void Version_two_upgrade_adds_activity_state_without_changing_character_fatigue()
    {
        var path = Unversioned.NewPath("activity-v2");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                DROP TABLE event_activation_state;
                DROP TABLE fatigue_consumption_settings;
                PRAGMA user_version=2;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp) VALUES(71,7,'kept',0,0,1,0);
                INSERT INTO character_fatigue(character_id,used,consumed_today,updated_at) VALUES(71,25,40,123456);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal(2L, migrated.From);
            Assert.Equal(CharacterDatabaseSchema.Version, migrated.To);
            Assert.Equal(25, Scalar(path, "SELECT used FROM character_fatigue WHERE character_id=71"));
            Assert.Equal(40, Scalar(path, "SELECT consumed_today FROM character_fatigue WHERE character_id=71"));
            Assert.Equal(123456, Scalar(path, "SELECT updated_at FROM character_fatigue WHERE character_id=71"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM fatigue_consumption_settings"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM event_activation_state"));
            _ = Unversioned.Open(path);
            Assert.Null(Migrator.Migrate(path, backup: false).Backup);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_three_upgrade_opens_the_slots_of_characters_that_finished_649_or_650()
    {
        var path = Unversioned.NewPath("slot-expansion-v3");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                PRAGMA user_version=3;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,ex_equip_slot_stat) VALUES
                    (71,7,'none',0,0,70,0,0),(72,7,'support',0,0,70,0,0),(73,7,'stone',0,0,70,0,0),
                    (74,7,'both',0,0,70,0,0),(75,7,'kept',0,0,70,0,2),(76,7,'carried',0,0,70,0,0);
                INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES
                    (71,674,1),(72,649,1),(73,650,1),(74,649,1),(74,650,1),(75,649,1),(76,649,0);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal(3L, migrated.From);
            Assert.Equal(CharacterDatabaseSchema.Version, migrated.To);
            Assert.Equal(0, Scalar(path, "SELECT ex_equip_slot_stat FROM characters WHERE character_id=71"));
            Assert.Equal(1, Scalar(path, "SELECT ex_equip_slot_stat FROM characters WHERE character_id=72"));
            Assert.Equal(2, Scalar(path, "SELECT ex_equip_slot_stat FROM characters WHERE character_id=73"));
            Assert.Equal(3, Scalar(path, "SELECT ex_equip_slot_stat FROM characters WHERE character_id=74"));
            Assert.Equal(3, Scalar(path, "SELECT ex_equip_slot_stat FROM characters WHERE character_id=75"));
            Assert.Equal(0, Scalar(path, "SELECT ex_equip_slot_stat FROM characters WHERE character_id=76"));
            Assert.Equal(7, Scalar(path, "SELECT COUNT(*) FROM character_invisible_flags"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void Version_four_upgrade_adds_character_relations_and_keeps_the_characters()
    {
        var path = Unversioned.NewPath("relations-v4");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                DROP TABLE character_relations;
                PRAGMA user_version=4;
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp) VALUES
                    (71,7,'first',3,1,61,123),(72,7,'second',4,0,62,456);
                """);
            var migrated = Migrator.Migrate(path, backup: false);
            Assert.Equal(4L, migrated.From);
            Assert.Equal(CharacterDatabaseSchema.Version, migrated.To);
            Assert.Equal(2, Scalar(path, "SELECT COUNT(*) FROM characters"));
            Assert.Equal(123, Scalar(path, "SELECT exp FROM characters WHERE character_id=71 AND name='first' AND level=61"));
            Assert.Equal(1, Scalar(path, "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_character_relations_target'"));
            Execute(path, "INSERT INTO character_relations(owner_id,target_id,kind,created_at) VALUES(71,72,1,1800000000),(72,71,2,1800000001);");
            Assert.Equal(2, Scalar(path, "SELECT COUNT(*) FROM character_relations"));
            _ = Unversioned.Open(path);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

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
    [InlineData(CharacterDatabaseSchema.Version + 1)]
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
