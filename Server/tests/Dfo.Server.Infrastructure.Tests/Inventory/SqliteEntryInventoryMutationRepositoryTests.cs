using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Infrastructure.Inventory;
using Dfo.Server.Infrastructure.Characters;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Tests.Inventory;

public sealed class SqliteEntryInventoryMutationRepositoryTests
{
    [Fact]
    public async Task DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteInventoryEntryRepository(path);
            _ = new SqliteCharacterRepository(path);
            await SeedCharacterAsync(path, 6, 60);
            await SeedCharacterAsync(path, 7, 70);
            await SeedAvatarAsync(path, 60, 9, 100, 1000, new(1, 2, new byte[30], 3, 4, 5));
            await SeedAvatarAsync(path, 60, 10, 200, 2000, new(6, 7, new byte[30], 8, 9, 10));
            await SeedAvatarAsync(path, 70, 9, 900, 9000, new(11, 12, new byte[30], 13, 14, 15));

            var loaded = await repository.LoadCharacterAsync(
                new AccountId(6), new CharacterId(60), CancellationToken.None);
            var loadedAvatar = loaded.CharacterItems.Single(value => value.Core.Value == 1000);
            var updated = new InventoryItemState(
                loadedAvatar.StorageEntryId, 11, loadedAvatar.Core,
                new InventoryAvatarDetailState(
                    21, 22, Enumerable.Repeat((byte)23, 30).ToArray(), 24, 25, 26));
            var inserted = updated with
            {
                StorageEntryId = 0,
                SlotIndex = 12,
                Core = updated.Core with { ItemId = 300, Value = 3000 },
                AvatarDetail = new InventoryAvatarDetailState(
                    31, 32, Enumerable.Repeat((byte)33, 30).ToArray(), 34, 35, 36),
            };
            var session = SelectedSessionIdentity.Create(
                new AccountId(6), new CharacterId(60), new NetworkConnectionId("Game:avatar"), 1, 10011);
            var inventory = ItemLease(new CharacterId(60), [updated, inserted]);

            await repository.SaveAsync(
                new EntryInventoryLeaseSnapshot(session, inventory, 1, true), CancellationToken.None);

            var saved = await repository.LoadCharacterAsync(
                new AccountId(6), new CharacterId(60), CancellationToken.None);
            Assert.Equal([1000, 3000], saved.CharacterItems.Select(value => value.Core.Value).Order());
            Assert.Equivalent(updated.AvatarDetail,
                saved.CharacterItems.Single(value => value.Core.Value == 1000).AvatarDetail);
            Assert.Equivalent(inserted.AvatarDetail,
                saved.CharacterItems.Single(value => value.Core.Value == 3000).AvatarDetail);
            await using (var connection = Open(path))
                Assert.Equal(0L, await ScalarAsync(connection,
                    "SELECT COUNT(*) FROM character_avatar_detail WHERE item_uid=2000"));

            var collision = inserted with
            {
                StorageEntryId = 0,
                SlotIndex = 13,
                Core = inserted.Core with { ItemId = 900, Value = 9000 },
            };
            await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.SaveAsync(
                new EntryInventoryLeaseSnapshot(session, ItemLease(new CharacterId(60), [collision]), 2, true),
                CancellationToken.None));

            var afterRollback = await repository.LoadCharacterAsync(
                new AccountId(6), new CharacterId(60), CancellationToken.None);
            Assert.Equal([1000, 3000], afterRollback.CharacterItems.Select(value => value.Core.Value).Order());
            var other = await repository.LoadCharacterAsync(
                new AccountId(7), new CharacterId(70), CancellationToken.None);
            Assert.Equal(15, Assert.Single(other.CharacterItems).AvatarDetail!.DeleteDate);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DirtyLeaseSave_IsAuthoritativeForDeleteMoveUpdateInsertAndRollsBackWrongOwner()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteInventoryEntryRepository(path);
            _ = new SqliteCharacterRepository(path);
            await SeedCharacterAsync(path, 6, 60);
            await SeedItemAsync(path, 60, 65, 100, 1);
            await SeedItemAsync(path, 60, 66, 200, 2);
            var loaded = await repository.LoadCharacterAsync(
                new AccountId(6), new CharacterId(60), CancellationToken.None);
            var first = loaded.CharacterItems.Single(value => value.Core.ItemId == 100);
            var updatedCore = first.Core with { Value = 9 };
            var insertedCore = first.Core with { ItemId = 300, Value = 3 };
            var inventory = ItemLease(new CharacterId(60),
                [new InventoryItemState(first.StorageEntryId, 70, updatedCore),
                    new InventoryItemState(0, 71, insertedCore)]);
            var session = SelectedSessionIdentity.Create(
                new AccountId(6), new CharacterId(60), new NetworkConnectionId("Game:lease"), 1, 10011);

            await repository.SaveAsync(
                new EntryInventoryLeaseSnapshot(session, inventory, 1, true), CancellationToken.None);
            var saved = await repository.LoadCharacterAsync(
                new AccountId(6), new CharacterId(60), CancellationToken.None);
            Assert.Equal([100, 300], saved.CharacterItems.Select(value => value.Core.ItemId).Order());
            Assert.Equal(70, saved.CharacterItems.Single(value => value.Core.ItemId == 100).SlotIndex);
            Assert.Equal(9, saved.CharacterItems.Single(value => value.Core.ItemId == 100).Core.Value);

            var wrong = session with { AccountId = new AccountId(999) };
            await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.SaveAsync(
                new EntryInventoryLeaseSnapshot(wrong, inventory with { Containers = [] }, 2, true),
                CancellationToken.None));
            Assert.Equal(2, (await repository.LoadCharacterAsync(
                new AccountId(6), new CharacterId(60), CancellationToken.None)).CharacterItems.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DailyRefill_CommitsGrantAndClaimTogetherAndSecondRunIsNoOp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteInventoryEntryRepository(path);
            _ = new SqliteCharacterRepository(path);
            await SeedCharacterAsync(path, accountId: 7, characterId: 70);
            var repository = new SqliteEntryInventoryMutationRepository(path);
            var rule = new DailyRefillRule(1234, 5, DateTimeOffset.MaxValue,
                DailyRefillMode.RefillToTarget, 100, 2, 65, 120);

            var first = await repository.ApplyDailyRefillAsync(
                new AccountId(7), new CharacterId(70), [rule], PreparedAt, CancellationToken.None);
            var second = await repository.ApplyDailyRefillAsync(
                new AccountId(7), new CharacterId(70), [rule], PreparedAt, CancellationToken.None);

            var grant = Assert.Single(first.Grants);
            Assert.Equal(65, grant.SlotIndex);
            Assert.Equal(5, grant.GrantedCount);
            Assert.Equal(5, grant.CommittedCount);
            Assert.Empty(second.Grants);
            await using var connection = Open(path);
            Assert.Equal(5, await ReadItemCountAsync(connection, 70, 1234));
            Assert.Equal(2L, await ScalarAsync(connection,
                "SELECT hex(substr(item_core,1,1)) FROM character_new_items WHERE character_id=70 AND item_core IS NOT NULL"));
            Assert.Equal("FFFFFFFF", await ScalarStringAsync(connection,
                "SELECT hex(substr(item_core,22,4)) FROM character_new_items WHERE character_id=70"));
            Assert.Equal(1L, await ScalarAsync(connection,
                "SELECT value FROM character_daily_counters WHERE character_id=70 AND counter_key='pvf_daily_refill_item_1234'"));
        }
        finally { File.Delete(path); }
    }

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task DailyRefill_ClaimsZeroGrantButOwnershipFailureRollsBackAll()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteInventoryEntryRepository(path);
            _ = new SqliteCharacterRepository(path);
            await SeedCharacterAsync(path, 8, 80);
            await SeedItemAsync(path, 80, 0, 2222, 5);
            var repository = new SqliteEntryInventoryMutationRepository(path);
            var rule = new DailyRefillRule(2222, 2, DateTimeOffset.MaxValue,
                DailyRefillMode.RefillToTarget, 100, 2, 65, 120);

            var result = await repository.ApplyDailyRefillAsync(
                new AccountId(8), new CharacterId(80), [rule], PreparedAt, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.ApplyDailyRefillAsync(
                    new AccountId(999), new CharacterId(80),
                    [rule with { ItemId = 3333 }], PreparedAt, CancellationToken.None));

            Assert.Empty(result.Grants);
            await using var connection = Open(path);
            Assert.Equal(1L, await ScalarAsync(connection,
                "SELECT value FROM character_daily_counters WHERE character_id=80 AND counter_key='pvf_daily_refill_item_2222'"));
            Assert.Equal(0L, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM character_daily_counters WHERE counter_key='pvf_daily_refill_item_3333'"));
        }
        finally { File.Delete(path); }
    }

    private static readonly DateTimeOffset PreparedAt =
        new(2026, 8, 12, 10, 0, 0, TimeSpan.FromHours(8));

    private static async Task SeedCharacterAsync(string path, int accountId, int characterId)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO characters(character_id,account_id,name) VALUES(@cid,@aid,'entry')";
        command.Parameters.AddWithValue("@cid", characterId); command.Parameters.AddWithValue("@aid", accountId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedItemAsync(string path, int characterId, int slot, int itemId, int count)
    {
        var core = new byte[82]; core[0] = 6; core[66] = byte.MaxValue;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1, 4), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5, 4), count);
        await using var connection = Open(path); await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,slot_index,item_core) VALUES('character',@cid,@cid,0,@slot,@core)";
        command.Parameters.AddWithValue("@cid", characterId); command.Parameters.AddWithValue("@slot", slot);
        command.Parameters.AddWithValue("@core", core); await command.ExecuteNonQueryAsync();
    }

    private static CharacterItemLeaseState ItemLease(
        CharacterId characterId, IReadOnlyList<InventoryItemState> items) => new(
        characterId, [new InventoryContainerState(InventoryContainerType.Main, 0, items)]);

    private static async Task SeedAvatarAsync(
        string path, int characterId, int slot, int itemId, int avatarUid,
        InventoryAvatarDetailState detail)
    {
        var core = new byte[82];
        core[0] = 8;
        core[66] = byte.MaxValue;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1, 4), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5, 4), avatarUid);
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,slot_index,item_core)
            VALUES('character',@cid,@cid,0,@slot,@core);
            INSERT INTO character_avatar_detail(item_uid,owner_id,character_id,item_id,expire_date,
              clear_avatar_id,jewel_socket,color1,color2,delete_date)
            VALUES(@uid,@cid,@cid,@item,@expire,@clear,@socket,@color1,@color2,@delete);
            """;
        command.Parameters.AddWithValue("@cid", characterId);
        command.Parameters.AddWithValue("@slot", slot);
        command.Parameters.AddWithValue("@core", core);
        command.Parameters.AddWithValue("@uid", avatarUid);
        command.Parameters.AddWithValue("@item", itemId);
        command.Parameters.AddWithValue("@expire", detail.ExpireDate);
        command.Parameters.AddWithValue("@clear", detail.ClearAvatarId);
        command.Parameters.AddWithValue("@socket", detail.JewelSocket);
        command.Parameters.AddWithValue("@color1", detail.Color1);
        command.Parameters.AddWithValue("@color2", detail.Color2);
        command.Parameters.AddWithValue("@delete", detail.DeleteDate);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReadItemCountAsync(SqliteConnection connection, int characterId, int itemId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT item_core FROM character_new_items WHERE character_id=@cid";
        command.Parameters.AddWithValue("@cid", characterId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var core = (byte[])reader.GetValue(0);
            if (BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1, 4)) == itemId)
                return BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(5, 4));
        }
        return 0;
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}"); connection.Open(); return connection;
    }
}
