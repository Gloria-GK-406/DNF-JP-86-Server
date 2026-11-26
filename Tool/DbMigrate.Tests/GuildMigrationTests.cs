using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

public sealed class GuildMigrationTests
{
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
            Assert.Equal((19L, 20L), (result.From, result.To));
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

    private static object? Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
