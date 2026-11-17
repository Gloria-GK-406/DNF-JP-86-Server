using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

public sealed class GameplayParameterStorageRetirementTests
{
    [Fact]
    public void Version_eleven_retires_only_derived_effects_and_preserves_sources_ledgers_and_notification_progress()
    {
        var path = Unversioned.NewPath("parameters-v11");
        string? backup = null;
        try
        {
            _ = Unversioned.Open(path);
            Execute(path, """
                CREATE TABLE fatigue_consumption_settings(id INTEGER PRIMARY KEY,suspended INTEGER NOT NULL);
                INSERT INTO fatigue_consumption_settings VALUES(1,1);
                CREATE TABLE drop_draw_multiplier(entry_key TEXT PRIMARY KEY,multiplier INTEGER NOT NULL);
                INSERT INTO drop_draw_multiplier VALUES('world',100);
                INSERT INTO event_configuration VALUES(1,3,'source-configuration-preserved');
                INSERT INTO event_activation_state VALUES(1,0,2,123);
                INSERT INTO accounts(account_id,m_id) VALUES(7,'kept');
                INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp) VALUES(71,7,'kept',0,0,1,0);
                INSERT INTO character_fatigue(character_id,used,consumed_today,updated_at) VALUES(71,25,40,123456);
                CREATE TABLE extension_table(value TEXT);
                INSERT INTO extension_table VALUES('extension-preserved');
                PRAGMA user_version=11;
                """);
            var result = Migrator.Migrate(path);
            backup = result.Backup;
            Assert.Equal(11, result.From);
            Assert.Equal(CharacterDatabaseSchema.Version, result.To);
            Assert.True(File.Exists(backup));
            Assert.Equal(0L, Scalar(path, "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('fatigue_consumption_settings','drop_draw_multiplier')"));
            Assert.Equal("source-configuration-preserved", Scalar(path, "SELECT input FROM event_configuration WHERE event_id=1"));
            Assert.Equal(3L, Scalar(path, "SELECT revision FROM event_configuration WHERE event_id=1"));
            Assert.Equal(2L, Scalar(path, "SELECT configuration_revision FROM event_activation_state WHERE event_id=1"));
            Assert.Equal(25L, Scalar(path, "SELECT used FROM character_fatigue WHERE character_id=71"));
            Assert.Equal(40L, Scalar(path, "SELECT consumed_today FROM character_fatigue WHERE character_id=71"));
            Assert.Equal("extension-preserved", Scalar(path, "SELECT value FROM extension_table"));
            Assert.Null(Migrator.Migrate(path).Backup);
            Assert.Equal("ok", Scalar(path, "PRAGMA integrity_check"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            if (backup is not null) File.Delete(backup);
        }
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
