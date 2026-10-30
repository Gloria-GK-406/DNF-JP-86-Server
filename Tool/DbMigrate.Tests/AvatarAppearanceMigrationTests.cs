using Microsoft.Data.Sqlite;

namespace DbMigrate.Tests;

public sealed class AvatarAppearanceMigrationTests
{
    [Fact]
    public void Existing_positive_only_appearance_column_is_upgraded_without_losing_titles_or_item_identity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"avatar-migration-{Guid.NewGuid():N}.db");
        try
        {
            Unversioned.CreateUnstamped(path);
            using var connection = new SqliteConnection($"Data Source={path}"); connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                ALTER TABLE items DROP COLUMN appearance_item_id;
                ALTER TABLE items ADD COLUMN appearance_item_id INTEGER CHECK(appearance_item_id > 0);
                INSERT INTO items(item_uid,item_core,appearance_item_id) VALUES(123,zeroblob(82),26596);
                """;
            command.ExecuteNonQuery();
            Unversioned.Migrate(path);
            Unversioned.Migrate(path); // idempotent
            _ = Unversioned.Open(path);
            command.CommandText = "SELECT appearance_item_id FROM items WHERE item_uid=123";
            Assert.Equal(26596L, command.ExecuteScalar());
            command.CommandText = "UPDATE items SET appearance_item_id=0 WHERE item_uid=123";
            command.ExecuteNonQuery();
            command.CommandText = "UPDATE items SET appearance_item_id=-1 WHERE item_uid=123";
            Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('items') WHERE name='previous_appearance_item_id'";
            Assert.Equal(0L, command.ExecuteScalar());
            command.CommandText = "PRAGMA quick_check"; Assert.Equal("ok", command.ExecuteScalar());
            command.CommandText = "PRAGMA foreign_key_check"; Assert.Null(command.ExecuteScalar());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
