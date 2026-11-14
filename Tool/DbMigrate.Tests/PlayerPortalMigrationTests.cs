using Dfo.Server.Data.PlayerPortal;
using Dfo.Server.Persistence.PlayerPortal;
using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

/// <summary>Schema 9 to 10: the player portal arrives closed, and everything already there stays as it was.</summary>
public sealed class PlayerPortalMigrationTests
{
    /// <summary>Tables a version 9 database has rows in for this test, compared row by row before and after.</summary>
    private static readonly string[] Kept =
        ["accounts", "characters", "mailbox_messages", "mailbox_recipients", "mailbox_attachments", "gm_cera_operations", "gm_administrators", "auction_trades"];

    [Fact]
    public async Task Version_nine_upgrade_adds_the_player_portal_closed_and_keeps_everything_else()
    {
        var path = Unversioned.NewPath("player-portal-v9");
        var fresh = Unversioned.NewPath("player-portal-fresh");
        try
        {
            _ = Unversioned.Open(fresh);
            _ = Unversioned.Open(path);
            Execute(path, """
                DROP TABLE player_portal_settings;
                DROP TABLE player_portal_sessions;
                DROP TABLE player_portal_operations;
                ALTER TABLE gm_audit DROP COLUMN account_id;
                ALTER TABLE gm_audit DROP COLUMN actor_kind;
                PRAGMA user_version=9;
                INSERT INTO accounts(account_id,m_id,cera,token_cera) VALUES(7,'kept',1200,300),(8,'other',0,5);
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,slot_index,delete_flag,aura_skin_slot) VALUES
                    (71,7,'alive',3,1,70,123,0,0,1),(72,7,'deleted',4,0,62,456,1,1,0),(81,8,'theirs',1,0,86,9,0,0,0);
                INSERT INTO auction_trades(listing_id,market,item_id,quantity,price,fee,seller_character_id,buyer_character_id,settled_at) VALUES(5,0,3037,2,1000,30,71,81,1800000000000);
                INSERT INTO mailbox_messages(message_id,sender_name,receiver_character_id,title,body) VALUES(1,'GM',71,'hello','body');
                INSERT INTO mailbox_recipients(message_id,character_id) VALUES(1,71);
                INSERT INTO mailbox_attachments(message_id,item_template_id,item_count,gold_amount) VALUES(1,3037,2,500);
                INSERT INTO gm_administrators(id,login_name,password_record,enabled,permissions) VALUES(1,'admin','record',1,15);
                INSERT INTO gm_cera_operations(administrator_id,request_id,payload_hash,committed_at,receipt) VALUES(1,'abc','hash',1800000000000,'{}');
                INSERT INTO gm_audit(operation_id,occurred_at,action,administrator_id,administrator_name,target,outcome,reason) VALUES
                    ('op-1',1800000000000,'GrantCera',1,'admin','account 7','Succeeded','ok'),
                    ('op-2',1800000001000,'Login',NULL,NULL,'admin','Rejected','bad password');
                """);
            var before = Snapshot(path);
            var auditBefore = Rows(path, "SELECT operation_id,occurred_at,action,administrator_id,administrator_name,target,outcome,reason FROM gm_audit ORDER BY operation_id");

            var migrated = Migrator.Migrate(path, backup: false);

            Assert.Equal((9L, (long)CharacterDatabaseSchema.Version), (migrated.From, migrated.To));
            Assert.Equal(10, CharacterDatabaseSchema.Version);
            Assert.Equal(ShapeOf(fresh), ShapeOf(path));
            foreach (var table in new[] { "player_portal_settings", "player_portal_sessions", "player_portal_operations" })
                Assert.Equal(0, Scalar(path, $"SELECT COUNT(*) FROM {table}"));
            Assert.Equal(2, Scalar(path, "SELECT COUNT(*) FROM gm_audit WHERE actor_kind='gm' AND account_id IS NULL"));
            Assert.Equal(auditBefore,
                Rows(path, "SELECT operation_id,occurred_at,action,administrator_id,administrator_name,target,outcome,reason FROM gm_audit ORDER BY operation_id"));
            Assert.Equal(before, Snapshot(path));

            var settings = await new SqlitePlayerPortalPersistence(Unversioned.Open(path)).ReadSettingsAsync(default);
            Assert.Equal(PlayerPortalSettings.Default, settings);
            Assert.False(settings.Enabled);

            var again = Migrator.Migrate(path, backup: false);
            Assert.Equal(again.From, again.To);
            Assert.Null(again.Backup);
            Assert.Equal(ShapeOf(fresh), ShapeOf(path));
            Assert.Equal(before, Snapshot(path));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            File.Delete(fresh);
        }
    }

    /// <summary>
    /// A database that already has the audit columns -- one from before schema versions, whose missing tables the legacy
    /// step built from the current declaration -- goes through the step unchanged.
    /// </summary>
    [Fact]
    public void Version_nine_step_leaves_a_database_that_already_has_the_columns_as_it_was()
    {
        var path = Unversioned.NewPath("player-portal-current");
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, "INSERT INTO gm_audit(operation_id,occurred_at,action,target,outcome,reason,actor_kind,account_id) VALUES('p',1,'PlayerLogin','t','Succeeded','r','player',7);");
            var shape = ShapeOf(path);

            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                PlayerPortalUpgrade.Apply(connection);
                PlayerPortalUpgrade.Apply(connection);
            }

            Assert.Equal(shape, ShapeOf(path));
            Assert.Equal(1, Scalar(path, "SELECT COUNT(*) FROM gm_audit WHERE actor_kind='player' AND account_id=7"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static string[] Snapshot(string path) =>
        [.. Kept.SelectMany(table => Rows(path, $"SELECT * FROM {table} ORDER BY 1").Select(row => table + ": " + row))];

    private static string[] Rows(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "NULL" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        return [.. rows];
    }

    /// <summary>Every table's columns, every index and every trigger, in a fixed order (as in <see cref="SchemaVersionTests"/>).</summary>
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
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
