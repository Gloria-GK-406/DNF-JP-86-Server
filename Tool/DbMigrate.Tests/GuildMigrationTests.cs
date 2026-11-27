using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

public sealed class GuildMigrationTests
{
    [Fact]
    public void Upgrading_v31_adds_red_packet_ledger_and_preserves_gifts()
    {
        var path = Unversioned.NewPath("guild-red-packets-v31");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path, Pooling = false, ForeignKeys = true,
        }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                DROP TRIGGER guild_red_packets_mark_departure;
                DROP TABLE guild_red_packet_claims;
                DROP TABLE guild_red_packets;
                DROP TABLE guild_red_packet_purchases;
                INSERT INTO guild_gifts(sender_id,sender_name,recipient_id,recipient_name,sent_day,sent_at,
                    product_id,kind,content_value,duration_days,price)
                VALUES(40,'First',42,'Second',500,1500,3,1,10,1,200);
                PRAGMA user_version=31;
                """);
            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal((31L, (long)CharacterDatabaseSchema.Version), (result.From, result.To));
            Assert.Equal(200L, Execute(connection, "SELECT price FROM guild_gifts"));
            Assert.Equal(0L, Execute(connection, "SELECT count(*) FROM guild_red_packets"));
            Assert.Equal(0L, Execute(connection, "SELECT count(*) FROM guild_red_packet_claims"));
            Assert.Equal(0L, Execute(connection, "SELECT count(*) FROM guild_red_packet_purchases"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Upgrading_v30_adds_gift_ledger_and_preserves_alliance()
    {
        var path = Unversioned.NewPath("guild-gifts-v30");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path, Pooling = false, ForeignKeys = true,
        }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                DROP TRIGGER guild_gifts_recipient_disbanded;
                DROP TABLE guild_gifts;
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at)
                    VALUES(40,'First','FIRST','',1),(42,'Second','SECOND','',1);
                INSERT INTO guild_alliances(first_id,second_id) VALUES(40,42);
                PRAGMA user_version=30;
                """);

            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal((30L, (long)CharacterDatabaseSchema.Version), (result.From, result.To));
            Assert.Equal(1L, Execute(connection, "SELECT COUNT(*) FROM guild_alliances WHERE first_id=40 AND second_id=42"));
            Assert.Equal(0L, Execute(connection, "SELECT COUNT(*) FROM guild_gifts"));
            Execute(connection, """
                INSERT INTO guild_gifts(sender_id,sender_name,recipient_id,recipient_name,sent_day,sent_at,
                    product_id,kind,content_value,duration_days,price)
                VALUES(40,'First',42,'Second',500,1500,3,1,10,1,200),(42,'Second',40,'First',500,1500,3,1,10,1,200);
                DELETE FROM guilds WHERE guild_id=42;
                """);
            Assert.Equal(1L, Execute(connection, "SELECT voided FROM guild_gifts WHERE sender_id=40"));
            Assert.Equal(0L, Execute(connection, "SELECT voided FROM guild_gifts WHERE sender_id=42"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Upgrading_v29_adds_alliances_and_preserves_guilds()
    {
        var path = Unversioned.NewPath("guild-alliance-v29");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path, Pooling = false, ForeignKeys = true,
        }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                DROP TABLE guild_alliances;
                DROP TABLE guild_alliance_applications;
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at,gm_points)
                    VALUES(40,'First','FIRST','',1,900),(42,'Second','SECOND','',1,0),(43,'Third','THIRD','',1,0);
                PRAGMA user_version=29;
                """);

            var result = Migrator.Migrate(path, backup: false);

            Assert.Equal((29L, (long)CharacterDatabaseSchema.Version), (result.From, result.To));
            Assert.Equal(900L, Execute(connection, "SELECT gm_points FROM guilds WHERE guild_id=40"));
            Assert.Equal(0L, Execute(connection, "SELECT COUNT(*) FROM guild_alliances"));
            Assert.Equal(0L, Execute(connection, "SELECT COUNT(*) FROM guild_alliance_applications"));
            Execute(connection, """
                INSERT INTO guild_alliances(first_id,second_id) VALUES(40,42);
                INSERT INTO guild_alliance_applications(sender_id,target_id,message,applied_at) VALUES(43,42,'pending',5);
                """);
            Assert.Throws<SqliteException>(() => Execute(connection, "INSERT INTO guild_alliances VALUES(42,43)"));
            Assert.Throws<SqliteException>(() => Execute(connection, "INSERT INTO guild_alliances VALUES(43,42)"));
            Execute(connection, "DELETE FROM guilds WHERE guild_id=42");
            Assert.Equal(0L, Execute(connection, "SELECT COUNT(*) FROM guild_alliances"));
            Assert.Equal(0L, Execute(connection, "SELECT COUNT(*) FROM guild_alliance_applications"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Upgrading_v28_defaults_member_memos_to_empty_and_preserves_membership()
    {
        var path = Unversioned.NewPath("guild-memo-v28");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            if (Execute(connection, "SELECT COUNT(*) FROM pragma_table_info('guild_members') WHERE name='memo'") is 1L)
                Execute(connection, "ALTER TABLE guild_members DROP COLUMN memo");
            Execute(connection, """
                INSERT INTO accounts(account_id,m_id) VALUES(1,'guild-memo');
                INSERT INTO characters(character_id,account_id,name) VALUES(11,1,'Leader'),(12,1,'Member');
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at) VALUES(42,'Iris','iris','',1);
                INSERT INTO guild_members(character_id,guild_id,is_leader,joined_at,donation_points,member_rank)
                    VALUES(11,42,1,1,50,4),(12,42,0,1,0,2);
                PRAGMA user_version=28;
                """);

            var result = Migrator.Migrate(path, backup: false);

            Assert.Equal(28L, result.From);
            Assert.Equal(1L, Execute(connection, """
                SELECT COUNT(*) FROM guild_members
                WHERE character_id=11 AND guild_id=42 AND is_leader=1 AND donation_points=50 AND memo=''
                """));
            Assert.Equal(1L, Execute(connection, """
                SELECT COUNT(*) FROM guild_members WHERE character_id=12 AND guild_id=42 AND member_rank=2 AND memo=''
                """));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Upgrading_v26_adds_an_empty_board_and_preserves_existing_guilds()
    {
        var path = Unversioned.NewPath("guild-board-v26");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                DROP TABLE guild_board_posts;
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at) VALUES(42,'Iris','iris','Before',1);
                PRAGMA user_version=26;
                """);
            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal((26L, (long)CharacterDatabaseSchema.Version), (result.From, result.To));
            Assert.Equal(0L, Execute(connection, "SELECT COUNT(*) FROM guild_board_posts"));
            Assert.Equal(1L, Execute(connection, "SELECT COUNT(*) FROM guilds WHERE guild_id=42 AND promotion='Before'"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Upgrading_v23_preserves_expansion_and_adds_empty_guild_item_custody()
    {
        var path = Unversioned.NewPath("guild-warehouse-v23");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                DROP TABLE guild_warehouse_items;
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at)
                    VALUES(42,'Iris','iris','Before',1);
                INSERT INTO guild_warehouse(guild_id,capacity) VALUES(42,24);
                PRAGMA user_version=23;
                """);
            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal((23L, (long)CharacterDatabaseSchema.Version), (result.From, result.To));
            Assert.Equal(24L, Execute(connection, "SELECT capacity FROM guild_warehouse WHERE guild_id=42"));
            Assert.Equal(0L, Execute(connection, "SELECT COUNT(*) FROM guild_warehouse_items"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Upgrading_v21_adds_empty_guild_contents_and_preserves_spent_gm()
    {
        var path = Unversioned.NewPath("guild-content-v21");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                DROP TABLE guild_contents;
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at,gm_points,spent_gm_points,mark_id)
                    VALUES(42,'Iris','iris','Before',1,98000,2000,7);
                PRAGMA user_version=21;
                """);
            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal((21L, (long)CharacterDatabaseSchema.Version), (result.From, result.To));
            Assert.Equal(0L, Execute(connection, "SELECT COUNT(*) FROM guild_contents;"));
            Assert.Equal(1L, Execute(connection,
                "SELECT COUNT(*) FROM guilds WHERE guild_id=42 AND gm_points=98000 AND spent_gm_points=2000 AND mark_id=7;"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Upgrading_v20_adds_default_mark_and_preserves_existing_gm_and_promotion()
    {
        var path = Unversioned.NewPath("guild-appearance-v20");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                ALTER TABLE guilds DROP COLUMN announcement;
                ALTER TABLE guilds DROP COLUMN mark_id;
                ALTER TABLE guilds DROP COLUMN spent_gm_points;
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at,gm_points)
                    VALUES(42,'Iris','iris','Before',1,50);
                PRAGMA user_version=20;
                """);

            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal((20L, (long)CharacterDatabaseSchema.Version), (result.From, result.To));
            Assert.Equal(1L, Execute(connection, """
                SELECT COUNT(*) FROM guilds
                WHERE guild_id=42 AND announcement='' AND mark_id=1 AND spent_gm_points=0 AND gm_points=50
                    AND promotion='Before';
                """));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Upgrading_v19_sets_ordinary_ranks_without_changing_leader_identity()
    {
        var path = Unversioned.NewPath("guild-appointment-v19");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                ALTER TABLE guild_members DROP COLUMN member_rank;
                INSERT INTO accounts(account_id,m_id) VALUES(1,'guild-rank');
                INSERT INTO characters(character_id,account_id,name) VALUES(11,1,'Leader'),(12,1,'Member');
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at,gm_points)
                    VALUES(42,'Iris','iris','',1,50);
                INSERT INTO guild_members(character_id,guild_id,is_leader,joined_at,donation_points)
                    VALUES(11,42,1,1,50),(12,42,0,1,0);
                INSERT INTO guild_rank_settings(guild_id,rank,permission_mask) VALUES(42,2,2048);
                PRAGMA user_version=19;
                """);

            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal((19L, (long)CharacterDatabaseSchema.Version), (result.From, result.To));
            Assert.Equal(11L, Execute(connection, "SELECT character_id FROM guild_members WHERE is_leader=1"));
            Assert.Equal(2L, Execute(connection, "SELECT COUNT(*) FROM guild_members WHERE member_rank=4"));
            Assert.Equal(2048L, Execute(connection, "SELECT permission_mask FROM guild_rank_settings WHERE rank=2"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Upgrading_v18_preserves_guild_attendance_and_adds_rank_settings()
    {
        var path = Unversioned.NewPath("guild-management-v18");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                DROP TABLE guild_rank_settings;
                INSERT INTO accounts(account_id,m_id) VALUES(1,'guild-management');
                INSERT INTO characters(character_id,account_id,name) VALUES(11,1,'Leader');
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at,gm_points)
                    VALUES(42,'Iris','iris','Before',1,50);
                INSERT INTO guild_members(character_id,guild_id,is_leader,joined_at,donation_points)
                    VALUES(11,42,1,1,50);
                INSERT INTO guild_attendance(guild_id, day, character_id) VALUES(42,739945,11);
                PRAGMA user_version=18;
                """);

            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal((18L, (long)CharacterDatabaseSchema.Version), (result.From, result.To));
            Assert.Equal("Before", Execute(connection, "SELECT promotion FROM guilds WHERE guild_id=42"));
            Assert.Equal(50L, Execute(connection, "SELECT gm_points FROM guilds WHERE guild_id=42"));
            Assert.Equal(1L, Execute(connection, "SELECT COUNT(*) FROM guild_attendance WHERE guild_id=42"));
            Assert.Equal(0L, Execute(connection, "SELECT COUNT(*) FROM guild_rank_settings"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Upgrading_v17_preserves_the_guild_and_donations_and_adds_empty_attendance()
    {
        var path = Unversioned.NewPath("guild-attendance-v17");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                DROP TABLE guild_attendance;
                INSERT INTO accounts(account_id,m_id) VALUES(1,'guild-attendance');
                INSERT INTO characters(character_id,account_id,name) VALUES(11,1,'Leader');
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at,gm_points)
                    VALUES(42,'Iris','iris','',1,50);
                INSERT INTO guild_members(character_id,guild_id,is_leader,joined_at,donation_points)
                    VALUES(11,42,1,1,50);
                INSERT INTO guild_gold_donations(guild_id,donor_character_id,donor_name,gold,points,donated_at)
                    VALUES(42,11,X'4c6561646572',50000,50,1);
                PRAGMA user_version=17;
                """);

            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal((17L, (long)CharacterDatabaseSchema.Version), (result.From, result.To));
            Assert.Equal(50L, Execute(connection, "SELECT gm_points FROM guilds WHERE guild_id=42"));
            Assert.Equal(50000L, Execute(connection, "SELECT gold FROM guild_gold_donations"));
            Assert.Equal(0L, Execute(connection, "SELECT COUNT(*) FROM guild_attendance"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Upgrading_v16_keeps_donation_totals_and_starts_with_an_empty_history()
    {
        var path = Unversioned.NewPath("guild-donation-history-v16");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                DROP TABLE guild_gold_donations;
                INSERT INTO accounts(account_id,m_id) VALUES(1,'guild-donation');
                INSERT INTO characters(character_id,account_id,name) VALUES(11,1,'Leader');
                INSERT INTO character_stats(character_id,carried_gold) VALUES(11,600000);
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at,gm_points)
                    VALUES(42,'Iris','iris','Hello',1,70);
                INSERT INTO guild_members(character_id,guild_id,is_leader,joined_at,donation_points)
                    VALUES(11,42,1,1,40);
                PRAGMA user_version=16;
                """);

            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal((16L, (long)CharacterDatabaseSchema.Version), (result.From, result.To));
            Assert.Equal(70L, Execute(connection, "SELECT gm_points FROM guilds WHERE guild_id=42"));
            Assert.Equal(40L, Execute(connection, "SELECT donation_points FROM guild_members WHERE character_id=11"));
            Assert.Equal(600_000L, Execute(connection, "SELECT carried_gold FROM character_stats"));
            Assert.Equal(0L, Execute(connection, "SELECT COUNT(*) FROM guild_gold_donations"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Upgrading_v15_preserves_guilds_and_role_gold_and_initializes_donation_points()
    {
        var path = Unversioned.NewPath("guild-donation-v15");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                ALTER TABLE guilds DROP COLUMN gm_points;
                ALTER TABLE guild_members DROP COLUMN donation_points;
                INSERT INTO accounts(account_id,m_id) VALUES(1,'guild-donation');
                INSERT INTO characters(character_id,account_id,name) VALUES(11,1,'Leader'),(12,1,'Member');
                INSERT INTO character_stats(character_id,carried_gold) VALUES(11,600000),(12,10000);
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at,recommended_channel)
                    VALUES(42,'Iris','iris','Hello',1,10);
                INSERT INTO guild_members(character_id,guild_id,is_leader,joined_at) VALUES(11,42,1,1),(12,42,0,1);
                PRAGMA user_version=15;
                """);

            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal((15L, (long)CharacterDatabaseSchema.Version), (result.From, result.To));
            Assert.Equal(0L, Execute(connection, "SELECT gm_points FROM guilds WHERE guild_id=42 AND recommended_channel=10"));
            Assert.Equal(2L, Execute(connection, "SELECT COUNT(*) FROM guild_members WHERE guild_id=42 AND donation_points=0"));
            Assert.Equal(11L, Execute(connection, "SELECT character_id FROM guild_members WHERE guild_id=42 AND is_leader=1"));
            Assert.Equal(610_000L, Execute(connection, "SELECT SUM(carried_gold) FROM character_stats WHERE character_id IN (11,12)"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Upgrading_existing_guilds_preserves_the_leader_and_defaults_to_no_channel()
    {
        var path = Unversioned.NewPath("guild-channel-v14");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                ALTER TABLE guilds DROP COLUMN recommended_channel;
                INSERT INTO accounts(account_id,m_id) VALUES(1,'guild-channel');
                INSERT INTO characters(character_id,account_id,name) VALUES(11,1,'Leader');
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at) VALUES(42,'Iris','iris','Hello',1);
                INSERT INTO guild_members(character_id,guild_id,is_leader,joined_at) VALUES(11,42,1,1);
                PRAGMA user_version=14;
                """);

            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal(14, result.From);
            Assert.Equal(CharacterDatabaseSchema.Version, result.To);
            Assert.Equal(0L, Execute(connection, "SELECT recommended_channel FROM guilds WHERE guild_id=42 AND name='Iris'"));
            Assert.Equal(1L, Execute(connection, "SELECT is_leader FROM guild_members WHERE guild_id=42 AND character_id=11"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuildStorageExistsOnFreshAndUpgradedDatabases(bool upgrade)
    {
        var path = Unversioned.NewPath("guild-v13");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            if (upgrade)
            {
                Execute(connection, """
                    DROP TABLE IF EXISTS guild_gold_donations;
                    DROP TABLE IF EXISTS guild_applications;
                    DROP TABLE IF EXISTS guild_members;
                    DROP TABLE IF EXISTS guilds;
                    INSERT INTO accounts(account_id,m_id,cera) VALUES(1,'migration-example',300);
                    INSERT INTO characters(character_id,account_id,name) VALUES(11,1,'Existing');
                    INSERT INTO character_stats(character_id,carried_gold) VALUES(11,9080);
                    PRAGMA user_version=13;
                    """);
                var result = Migrator.Migrate(path, backup: false);
                Assert.Equal(13, result.From);
                Assert.Equal(CharacterDatabaseSchema.Version, result.To);
                Assert.Equal("Existing", Execute(connection, "SELECT name FROM characters WHERE character_id=11 AND account_id=1"));
                Assert.Equal(300L, Execute(connection, "SELECT cera FROM accounts WHERE account_id=1"));
                Assert.Equal(9080L, Execute(connection, "SELECT carried_gold FROM character_stats WHERE character_id=11"));
                Assert.Null(Migrator.Migrate(path).Backup);
            }

            Assert.Equal(3L, Execute(connection, """
                SELECT COUNT(*) FROM sqlite_master WHERE type='table'
                AND name IN ('guilds','guild_members','guild_applications');
                """));
            Assert.Equal(0L, Execute(connection, "SELECT COUNT(*) FROM guild_members"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Upgrading_from_v24_starts_dungeon_progress_empty_and_preserves_guild_assets()
    {
        var path = Unversioned.NewPath("guild-dungeon-v24");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            CharacterDatabaseSchema.Ensure(connectionString);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Execute(connection, """
                DROP TABLE guild_dungeon_clears;
                DROP TABLE guild_dungeon_progress;
                INSERT INTO accounts(account_id,m_id) VALUES(1,'guild-dungeon');
                INSERT INTO characters(character_id,account_id,name) VALUES(11,1,'GuildExplorer');
                INSERT INTO guilds(guild_id,name,name_key,promotion,created_at,gm_points) VALUES(42,'Iris','iris','Hello',1,120);
                INSERT INTO guild_members(character_id,guild_id,is_leader,joined_at) VALUES(11,42,1,1);
                INSERT INTO guild_warehouse(guild_id,capacity) VALUES(42,24);
                PRAGMA user_version=24;
                """);

            var result = Migrator.Migrate(path, backup: false);
            Assert.Equal(24, result.From);
            Assert.Equal(CharacterDatabaseSchema.Version, result.To);
            Assert.Equal(0L, Execute(connection, "SELECT COUNT(*) FROM guild_dungeon_progress"));
            Assert.Equal(0L, Execute(connection, "SELECT COUNT(*) FROM guild_dungeon_clears"));
            Assert.Equal(120L, Execute(connection, "SELECT gm_points FROM guilds WHERE guild_id=42"));
            Assert.Equal(1L, Execute(connection, "SELECT is_leader FROM guild_members WHERE guild_id=42 AND character_id=11"));
            Assert.Equal(24L, Execute(connection, "SELECT capacity FROM guild_warehouse WHERE guild_id=42"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static object? Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
