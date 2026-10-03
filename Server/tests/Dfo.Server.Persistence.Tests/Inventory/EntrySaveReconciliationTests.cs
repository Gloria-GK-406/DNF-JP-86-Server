using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>Explicit custody operations preserve kept items and remove only named items.</summary>
public sealed class EntrySaveReconciliationTests
{
    private const int Account = 7;

    private const int Character = 70;

    private const int SwordId = 4000;

    private const int SecondSwordId = 4001;

    private const int PotionId = 1000;

    [Fact]
    public async Task ExplicitMovesPreserveNamedAndPileItemsAndDeleteOnlyTheSelectedItem()
    {
        var path = NewDatabase();
        try
        {
            await SeedCharacterAsync(path);
            await SeedAsync(path, slot: 3, itemKind: 1, itemId: SwordId, value: 0);
            await SeedAsync(path, slot: 4, itemKind: 1, itemId: SecondSwordId, value: 0);
            await SeedAsync(path, slot: 65, itemKind: 2, itemId: PotionId, value: 5);

            var side = new InventorySide(path, new DeclaredArchive());
            var loaded = await side.InUseCaseAsync(token =>
                new CharacterInventoryEntryDomainService(side.CharacterEntries)
                    .LoadAsync(new AccountId(Account), new CharacterId(Character), token));

            var main = loaded.Containers.Single(
                container => container.ContainerType == InventoryContainerType.Main);
            var sword = main.Items.Single(item => item.Core.ItemId == SwordId);
            var potion = main.Items.Single(item => item.Core.ItemId == PotionId);
            var dropped = main.Items.Single(item => item.Core.ItemId == SecondSwordId);

            // Persisted world/template slots expose identities only for individual items.
            Assert.NotNull(sword.Identity);
            Assert.NotNull(dropped.Identity);
            Assert.Null(potion.Identity);
            Assert.Equal(sword.Identity!.Value.Value, await IdentityAtAsync(path, 3));

            var named = sword.Identity!.Value;
            await side.InUseCaseAsync(async token =>
            {
                var items = side.Store.SessionIn(side.Scope.Current!);
                var mainId = ContainerId.ForCharacter(new CharacterId(Character), InventoryContainerType.Main);
                foreach (var (from, to) in new[] { (3, 7), (65, 66) })
                {
                    var moving = await items.TakeAsync((await items.GetAsync(mainId, from, token))!, null, token);
                    await items.PutAsync(mainId, to, moving, token);
                }
                await items.DeleteItemAsync(await items.TakeAsync(
                    (await items.GetAsync(mainId, 4, token))!, null, token), token);
            });

            // The sword is the same sword: the row that stood in slot 3 is the row now standing in
            // slot 7, under the identity it already had.
            Assert.Equal(named.Value, await IdentityAtAsync(path, 7));
            Assert.Null(await IdentityAtAsync(path, 3));

            // The pile keeps its quantity and destination without acquiring a business UID.
            Assert.Equal((PotionId, 5), await HeldAtAsync(path, 66));
            Assert.Null(await IdentityAtAsync(path, 65));

            // Only the explicitly deleted item disappears.
            Assert.Null(await IdentityAtAsync(path, 4));
            Assert.Equal(2, await RowCountAsync(path));
        }
        finally
        {
            Clean(path);
        }
    }

    [Fact]
    public async Task ExplicitDeletionOfEveryItemLeavesTheInventoryEmpty()
    {
        var path = NewDatabase();
        try
        {
            await SeedCharacterAsync(path);
            await SeedAsync(path, slot: 3, itemKind: 1, itemId: SwordId, value: 0);
            await SeedAsync(path, slot: 65, itemKind: 2, itemId: PotionId, value: 5);

            var side = new InventorySide(path, new DeclaredArchive());
            await side.InUseCaseAsync(async token =>
            {
                var items = side.Store.SessionIn(side.Scope.Current!);
                var mainId = ContainerId.ForCharacter(new CharacterId(Character), InventoryContainerType.Main);
                foreach (var held in await items.GetAllAsync(mainId, token))
                    await items.DeleteItemAsync(await items.TakeAsync(held, null, token), token);
            });

            Assert.Equal(0, await RowCountAsync(path));
        }
        finally
        {
            Clean(path);
        }
    }

    [Fact]
    public async Task PuttingOneRecordInTwoPositionsIsRefusedAndRollsBackTheMove()
    {
        var path = NewDatabase();
        try
        {
            await SeedCharacterAsync(path);
            await SeedAsync(path, slot: 3, itemKind: 1, itemId: SwordId, value: 0);

            var side = new InventorySide(path, new DeclaredArchive());
            var loaded = await side.InUseCaseAsync(token =>
                new CharacterInventoryEntryDomainService(side.CharacterEntries)
                    .LoadAsync(new AccountId(Account), new CharacterId(Character), token));
            var sword = loaded.Containers
                .Single(container => container.ContainerType == InventoryContainerType.Main)
                .Items.Single(item => item.Core.ItemId == SwordId);
            var named = sword.Identity!.Value;

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await side.InUseCaseAsync(async token =>
                {
                    var items = side.Store.SessionIn(side.Scope.Current!);
                    var mainId = ContainerId.ForCharacter(new CharacterId(Character), InventoryContainerType.Main);
                    var moving = await items.TakeAsync((await items.GetAsync(mainId, 3, token))!, null, token);
                    await items.PutAsync(mainId, 7, moving, token);
                    await items.PutAsync(mainId, 8, moving, token);
                }));

            Assert.Equal(named.Value, await IdentityAtAsync(path, 3));
            Assert.Equal(1, await RowCountAsync(path));
        }
        finally
        {
            Clean(path);
        }
    }

    private sealed class DeclaredArchive : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(
            int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(
                itemId is SwordId or SecondSwordId
                    ? new ItemPlacement(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 120))
                        { WorldClass = ItemWorldClass.Equipment }
                    : new ItemPlacement(2, 100, new(BusinessInventorySpace.Consumable, 0, 55)));
    }

    private static async Task<long?> IdentityAtAsync(string path, int slotIndex)
    {
        var found = await ScalarAsync(
            path,
            "SELECT core_uid FROM item_slots "
            + "WHERE owner_scope='character' AND owner_id=@cid "
            + "AND list_type=0 AND slot_index=@slot",
            command =>
            {
                command.Parameters.AddWithValue("@cid", Character);
                command.Parameters.AddWithValue("@slot", slotIndex);
            });
        return found is null or DBNull ? null : Convert.ToInt64(found, null);
    }

    private static async Task<(int ItemId, int Count)?> HeldAtAsync(string path, int slotIndex)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT held.item_core "
            + "FROM item_slots place JOIN items held ON held.item_uid=place.core_uid "
            + "WHERE place.owner_scope='character' AND place.owner_id=@cid "
            + "AND place.list_type=0 AND place.slot_index=@slot";
        command.Parameters.AddWithValue("@cid", Character);
        command.Parameters.AddWithValue("@slot", slotIndex);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var core = (byte[])reader.GetValue(0);
        return (BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(5, 4)));
    }

    private static async Task<int> RowCountAsync(string path) =>
        Convert.ToInt32(
            await ScalarAsync(
                path,
                "SELECT COUNT(*) FROM item_slots place JOIN items held ON held.item_uid=place.core_uid "
                + "WHERE place.owner_scope='character' AND place.owner_id=@cid",
                command => command.Parameters.AddWithValue("@cid", Character)),
            null);

    private static async Task SeedAsync(
        string path, int slot, byte itemKind, int itemId, int value)
    {
        var core = new byte[82];
        core[0] = itemKind;
        core[66] = byte.MaxValue;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1, 4), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5, 4), value);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(21, 4), -1);
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO items(item_core) VALUES(@core);"
            + "INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) "
            + "VALUES('character',@cid,0,@slot,@kind,CASE WHEN @kind='world' THEN last_insert_rowid() ELSE @id END,last_insert_rowid(),@count)";
        command.Parameters.AddWithValue("@kind", itemKind == 2 ? "template" : "world");
        command.Parameters.AddWithValue("@id", itemKind == 2 ? itemId : 0);
        command.Parameters.AddWithValue("@count", itemKind == 2 ? value : 1);
        command.Parameters.AddWithValue("@cid", Character);
        command.Parameters.AddWithValue("@slot", slot);
        command.Parameters.AddWithValue("@core", core);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedCharacterAsync(string path)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO characters(character_id,account_id,name) VALUES(@cid,@aid,'reconcile')";
        command.Parameters.AddWithValue("@cid", Character);
        command.Parameters.AddWithValue("@aid", Account);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(
        string path, string sql, Action<SqliteCommand> bind)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        bind(command);
        return await command.ExecuteScalarAsync();
    }

    private static string NewDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-reconcile-{Guid.NewGuid():N}.db");

        // The repositories own the schema; everything here only writes into it.
        _ = new SqliteDatabase(path, new DomainTransactionScope());
        return path;
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        return connection;
    }

    private static void Clean(string path)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A file the runtime still holds is left for the temp directory to reap.
        }
    }
}
