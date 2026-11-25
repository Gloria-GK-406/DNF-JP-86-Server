using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

public sealed class GuildMigrationTests
{
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
