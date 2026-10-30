using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

public sealed class SqliteItemPositionTransferTests
{
    private static readonly CharacterId Sender = new(60);
    private static readonly CharacterId Receiver = new(61);
    private static readonly ItemIdentity Identity = new(1);

    [Fact]
    public async Task TakeAndGrant_PreserveWorldRowAndCore()
    {
        var path = await SeedAsync();
        try
        {
            var store = new SqliteItemDomainStore(path, TestRequired.NoContentsEvents);
            var before = await WorldRowAsync(path);
            var take = new DomainTransaction();
            var session = store.SessionIn(take);
            var owned = await session.GetAsync(ContainerId.ForCharacter(Sender, InventoryContainerType.Main), 9, default);
            Assert.NotNull(owned);
            var record = await session.TakeAsync(owned, null, default);
            await session.PutAsync(ContainerId.ForCharacter(Receiver, InventoryContainerType.Main), 9, record, default);
            await take.CommitAsync(default);
            Assert.Null(await SlotAsync(path, Sender));

            Assert.Equal(Identity.Value, await SlotAsync(path, Receiver));
            Assert.Equal(before, await WorldRowAsync(path));
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task RollbackAndOccupiedTarget_LeaveOriginalPositionAndWorldRow()
    {
        var path = await SeedAsync(occupiedReceiver: true);
        try
        {
            var store = new SqliteItemDomainStore(path, TestRequired.NoContentsEvents);
            var before = await WorldRowAsync(path);
            var rollback = new DomainTransaction();
            var session = store.SessionIn(rollback);
            var owned = await session.GetAsync(ContainerId.ForCharacter(Sender, InventoryContainerType.Main), 9, default);
            Assert.NotNull(owned);
            var record = await session.TakeAsync(owned, null, default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.PutAsync(
                ContainerId.ForCharacter(Receiver, InventoryContainerType.Main), 9, record, default).AsTask());
            await rollback.RollbackAsync();

            Assert.Equal(Identity.Value, await SlotAsync(path, Sender));
            Assert.Equal(before, await WorldRowAsync(path));
        }
        finally { Delete(path); }
    }

    private static async Task<string> SeedAsync(bool occupiedReceiver = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-transfer-{Guid.NewGuid():N}.db");
        _ = new SqliteDatabase(path, TestRequired.TransactionScope());
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'sender'),(61,6,'receiver');
            INSERT INTO items(item_core,created_at,updated_at)
            VALUES(@core,'2020-01-02 03:04:05','2020-01-03 04:05:06');
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
            VALUES('character',60,0,9,'world',last_insert_rowid(),last_insert_rowid(),1);
            """;
        var core = new byte[82];
        core[0] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1, 4), 1001);
        core[21] = 0x7f;
        command.Parameters.AddWithValue("@core", core);
        await command.ExecuteNonQueryAsync();
        if (occupiedReceiver)
        {
            await using var blocked = connection.CreateCommand();
            blocked.CommandText = """
                INSERT INTO items(item_core) VALUES(@core);
                INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                VALUES('character',61,0,9,'world',last_insert_rowid(),last_insert_rowid(),1);
                """;
            blocked.Parameters.AddWithValue("@core", core);
            await blocked.ExecuteNonQueryAsync();
        }
        return path;
    }

    private static async Task<(string Core, string Created, string Updated)> WorldRowAsync(string path)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT hex(item_core),created_at,updated_at FROM items WHERE item_uid=1";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    private static async Task<int?> SlotAsync(string path, CharacterId owner)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=@owner";
        command.Parameters.AddWithValue("@owner", owner.Value);
        return await command.ExecuteScalarAsync() is long uid ? checked((int)uid) : null;
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        return connection;
    }

    private static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(path); }
        catch (IOException)
        {
            // The temp directory will reap a file still held briefly by SQLite on Windows.
        }
    }
}
