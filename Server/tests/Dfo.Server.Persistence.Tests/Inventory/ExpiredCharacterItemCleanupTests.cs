using System.Buffers.Binary;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Inventory;

public sealed class ExpiredCharacterItemCleanupTests
{
    [Fact]
    public async Task NextEntryRemovesOnlyExpiredOwnedItemsAndIsIdempotent()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"dfo-expired-entry-{Guid.NewGuid():N}.db");
            try
            {
                var scope = TestRequired.TransactionScope();
                var database = new SqliteDatabase(path, scope);
                var cleanup = new ExpiredCharacterItemCleanupDomainService(
                    new SqliteCharacterTable(database), new SqliteCharacterItemTable(database),
                    scope, TimeProvider.System, new SqliteItemDomainStore(path, TestRequired.NoContentsEvents));
                await SeedAsync(path);

                await using (var transaction = scope.Begin())
                {
                    Assert.True((await cleanup.CleanupAsync(
                        new AccountId(41), new CharacterId(57), CancellationToken.None)).Owned);
                    await transaction.CommitAsync(CancellationToken.None);
                }

                Assert.Equal(new long[] { 2L, 4L }, await ReadItemUidsAsync(path));
                Assert.Equal(new long[] { 2L, 4L }, await ReadSlotUidsAsync(path));

                await using (var transaction = scope.Begin())
                {
                    Assert.True((await cleanup.CleanupAsync(
                        new AccountId(41), new CharacterId(57), CancellationToken.None)).Owned);
                    await transaction.CommitAsync(CancellationToken.None);
                }
                Assert.Equal(new long[] { 2L, 4L }, await ReadItemUidsAsync(path));
                Assert.Equal(new long[] { 2L, 4L }, await ReadSlotUidsAsync(path));
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { File.Delete(path); }
                catch (IOException) { /* A remaining SQLite handle belongs to test cleanup. */ }
            }
        });
    }

    [Fact]
    public async Task WrongAccountCannotDeleteAnyItem()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"dfo-expired-owner-{Guid.NewGuid():N}.db");
            try
            {
                var scope = TestRequired.TransactionScope();
                var database = new SqliteDatabase(path, scope);
                var cleanup = new ExpiredCharacterItemCleanupDomainService(
                    new SqliteCharacterTable(database), new SqliteCharacterItemTable(database),
                    scope, TimeProvider.System, new SqliteItemDomainStore(path, TestRequired.NoContentsEvents));
                await SeedAsync(path);

                await using (var transaction = scope.Begin())
                {
                    Assert.False((await cleanup.CleanupAsync(
                        new AccountId(42), new CharacterId(57), CancellationToken.None)).Owned);
                    await transaction.CommitAsync(CancellationToken.None);
                }
                Assert.Equal(new long[] { 1L, 2L, 3L, 4L }, await ReadItemUidsAsync(path));
                Assert.Equal(new long[] { 1L, 2L, 3L, 4L }, await ReadSlotUidsAsync(path));
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { File.Delete(path); }
                catch (IOException) { /* A remaining SQLite handle belongs to test cleanup. */ }
            }
        });
    }

    private static async Task SeedAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO accounts(account_id,m_id) VALUES(41,'expiry');
            INSERT INTO characters(character_id,account_id,name,job,level)
            VALUES(57,41,'expiry',3,60);
            INSERT INTO items(item_uid,item_core,avatar_expire_date,avatar_clear_id,
                avatar_jewel_socket,avatar_color1,avatar_color2,avatar_delete_date)
            VALUES(1,{Core(8, 0)},1,0,zeroblob(30),0,0,0);
            INSERT INTO items(item_uid,item_core,avatar_expire_date,avatar_clear_id,
                avatar_jewel_socket,avatar_color1,avatar_color2,avatar_delete_date)
            VALUES(2,{Core(8, 0)},2147483647,0,zeroblob(30),0,0,0);
            INSERT INTO items(item_uid,item_core) VALUES(3,{Core(1, 1)});
            INSERT INTO items(item_uid,item_core) VALUES(4,{Core(1, 2147483647)});
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
            VALUES('character',57,1,4,'world',1,1,1),
                  ('character',57,1,5,'world',2,2,1),
                  ('character',57,0,6,'world',3,3,1),
                  ('character',57,0,7,'world',4,4,1);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static string Core(byte kind, int expiresAt)
    {
        var bytes = new byte[82];
        bytes[0] = kind;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), 2500);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(5), 4242);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(41), expiresAt);
        return $"X'{Convert.ToHexString(bytes)}'";
    }

    private static Task<long[]> ReadItemUidsAsync(string path) =>
        ReadUidsAsync(path, "SELECT item_uid FROM items ORDER BY item_uid");

    private static Task<long[]> ReadSlotUidsAsync(string path) =>
        ReadUidsAsync(path, "SELECT core_uid FROM item_slots ORDER BY core_uid");

    private static async Task<long[]> ReadUidsAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var itemUids = new List<long>();
        while (await reader.ReadAsync()) itemUids.Add(reader.GetInt64(0));
        return [.. itemUids];
    }
}
