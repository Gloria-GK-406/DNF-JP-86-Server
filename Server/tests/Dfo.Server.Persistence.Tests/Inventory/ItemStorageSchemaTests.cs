using Dfo.Server.Testing;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>Database constraints are checked directly, without a second item write API.</summary>
public sealed class ItemStorageSchemaTests
{
    [Fact]
    public async Task ItemsHaveNoPositionAndSlotsHaveOneShapeForBothKinds()
    {
        await WithDatabase(async connection =>
        {
            await using var columns = connection.CreateCommand();
            columns.CommandText = "SELECT name FROM pragma_table_info('items')";
            await using (var reader = await columns.ExecuteReaderAsync())
            {
                var names = new List<string>();
                while (await reader.ReadAsync()) names.Add(reader.GetString(0));
                Assert.Contains("item_uid", names);
                Assert.Contains("item_core", names);
                Assert.DoesNotContain("owner_scope", names);
                Assert.DoesNotContain("owner_id", names);
                Assert.DoesNotContain("list_type", names);
                Assert.DoesNotContain("slot_index", names);
            }
            await Execute(connection, """
                INSERT INTO items(item_core) VALUES(zeroblob(82)),(zeroblob(82));
                INSERT INTO item_slots VALUES('character',70,0,5,'world',1,1,1);
                INSERT INTO item_slots VALUES('character',70,0,6,'template',8474,2,42);
                """);
            await using var query = connection.CreateCommand();
            query.CommandText = "SELECT * FROM item_slots ORDER BY slot_index";
            await using var slots = await query.ExecuteReaderAsync();
            Assert.Equal(8, slots.FieldCount);
            Assert.Equal(["owner_scope", "owner_id", "list_type", "slot_index", "kind", "id", "core_uid", "count"],
                Enumerable.Range(0, slots.FieldCount).Select(slots.GetName));
            foreach (var expected in new object[][]
                {
                    ["character", 70L, 0L, 5L, "world", 1L, 1L, 1L],
                    ["character", 70L, 0L, 6L, "template", 8474L, 2L, 42L],
                })
            {
                Assert.True(await slots.ReadAsync());
                var actual = new object[slots.FieldCount];
                slots.GetValues(actual);
                Assert.Equal(expected, actual);
            }
            Assert.False(await slots.ReadAsync());
        });
    }

    [Theory]
    [InlineData("world", 1, 1, 2)]
    [InlineData("world", 1, 2, 1)]
    [InlineData("template", 8474, 1, 0)]
    [InlineData("template", 8474, 1, -1)]
    public async Task InvalidOccupancyIsRefused(string kind, int id, int coreUid, int count)
    {
        await WithDatabase(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO item_slots VALUES('character',70,0,5,@kind,@id,@uid,@count)";
            command.Parameters.AddWithValue("@kind", kind);
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@uid", coreUid);
            command.Parameters.AddWithValue("@count", count);
            var refusal = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(19, refusal.SqliteErrorCode);
        });
    }

    [Fact]
    public async Task OccupiedPositionCannotBeInsertedTwice()
    {
        await WithDatabase(async connection =>
        {
            const string insert = "INSERT INTO item_slots VALUES('character',70,0,5,'template',8474,1,3)";
            await Execute(connection, insert);
            var refusal = await Assert.ThrowsAsync<SqliteException>(() => Execute(connection, insert));
            Assert.Equal(19, refusal.SqliteErrorCode);
            await using var query = connection.CreateCommand();
            query.CommandText = "SELECT count FROM item_slots";
            Assert.Equal(3L, await query.ExecuteScalarAsync());
        });
    }

    private static async Task Execute(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WithDatabase(Func<SqliteConnection, Task> work)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-item-schema-{Guid.NewGuid():N}.db");
        try
        {
            var database = new SqliteDatabase(path, TestRequired.TransactionScope());
            await using var connection = new SqliteConnection(database.ConnectionString);
            await connection.OpenAsync();
            await work(connection);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
