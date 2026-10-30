using Dfo.Server.Testing;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

/// <summary>Entry projections preserve stored rows and agree with deltas built from current references.</summary>
public sealed class ContainerInventoryProjectionTests
{
    private static readonly DateTimeOffset PreparedAt =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly AccountId Account = new(6);

    private static readonly CharacterId Character = new(60);

    [Fact]
    public async Task EntrySnapshotPreservesMainRowsInSlotOrderAndExcludesOtherLists()
    {
        await using var storage = await SqliteStorage.CreateAsync();
        await storage.SeedAsync(listType: 0, slot: 121, itemId: 3001, count: 1, itemKind: 3);
        await storage.SeedAsync(listType: 0, slot: 3, itemId: 8474, count: 2, itemKind: 2);
        await storage.SeedAsync(listType: 0, slot: 65, itemId: 8475, count: 99, itemKind: 2);
        await storage.SeedAsync(listType: 2, slot: 0, itemId: 7777, count: 4, itemKind: 2);

        var fromLoader = await storage.LoadContainerAsync(InventoryContainerType.Main);
        var expected = new InventoryContainerState(InventoryContainerType.Main, fromLoader.Parameter,
            [new(null, 3, Core(8474, 2, 2)), new(null, 65, Core(8475, 99, 2)), new(null, 121, Core(3001, 1, 3))]);
        Assert.Equal(Encode(expected), Encode(fromLoader));
        Assert.Equal([3, 65, 121], fromLoader.Items.Select(item => item.SlotIndex));
    }

    [Fact]
    public async Task CurrentReferenceDeltaMatchesEntryProjection()
    {
        await using var storage = await SqliteStorage.CreateAsync();
        await storage.SeedAsync(listType: 0, slot: 65, itemId: 8475, count: 99, itemKind: 2);
        await storage.SeedAsync(listType: 0, slot: 3, itemId: 8474, count: 2, itemKind: 2);

        var fromLoader = await storage.LoadContainerAsync(InventoryContainerType.Main);
        var container = storage.OpenContainer(fromLoader.Parameter);
        var moved = Assert.IsType<ItemReference>(await container.GetAsync(3, CancellationToken.None));
        var fromContainer = new InventoryDeltaMessage(container.Type,
            [new InventoryItemState(moved.ItemUid, moved.SlotIndex, moved.Snapshot.ToCore())], PreparedAt).EncodeBody();
        var expected = new InventoryDeltaMessage(
            InventoryContainerType.Main,
            [.. fromLoader.Items.Where(item => item.SlotIndex == 3)],
            PreparedAt).EncodeBody();

        Assert.Equal(expected, fromContainer);
    }

    private static InventoryItemCoreState Core(int itemId, int count, byte kind)
    {
        var bytes = new byte[82];
        bytes[0] = kind;
        bytes[66] = byte.MaxValue;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(5), count);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(21), -1);
        return InventoryItemCore.Decode(bytes);
    }

    private static byte[] Encode(InventoryContainerState container) =>
        new InventorySnapshotMessage(container, [], [], 0, 0, PreparedAt).EncodeBody();

    private sealed class SqliteStorage : IAsyncDisposable
    {
        private SqliteStorage(string path)
        {
            Path = path;
            var side = new InventorySide(path);
            Entries = side.PerCall;
        }

        public string Path { get; }

        private ICharacterInventoryEntryRepository Entries { get; }

        public static async Task<SqliteStorage> CreateAsync()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-projection-{Guid.NewGuid():N}.db");
            _ = new SqliteDatabase(path, TestRequired.TransactionScope());
            var storage = new SqliteStorage(path);
            await using var connection = Open(path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'projection')";
            await command.ExecuteNonQueryAsync();
            return storage;
        }

        /// <summary>The container the two readers are compared over, at the loader's own shape.</summary>
        public IItemContainer OpenContainer(ushort parameter) =>
            new PersistentItemContainer(
                ContainerId.ForCharacter(Character, InventoryContainerType.Main),
                new ContainerShape(InventoryContainerType.Main, parameter),
                new SqliteItemDomainStore(Path, TestRequired.NoContentsEvents),
                TestRequired.TransactionScope(),
                new ItemPlacementRuleService(new NothingDeclared(), new NoWearables()),
                null!);

        private sealed class NothingDeclared : IItemPlacementDefinitionSource
        {
            public ValueTask<ItemPlacement?> GetAsync(
                int itemId, CancellationToken cancellationToken) =>
                ValueTask.FromResult<ItemPlacement?>(null);
        }

        private sealed class NoWearables : IEquipmentSlotDefinitionSource
        {
            public ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken cancellationToken) =>
                ValueTask.FromResult<BusinessSlot?>(null);
        }

        public async Task<InventoryContainerState> LoadContainerAsync(
            InventoryContainerType containerType)
        {
            var loaded = await new CharacterInventoryEntryDomainService(Entries, TestRequired.NoLegacyWornItems(), containers: TestRequired.Absent<Dfo.Server.Abstractions.Inventory.ICharacterContainerDomainService>())
                .LoadAsync(Account, Character, CancellationToken.None);
            return loaded.Containers.Single(value => value.ContainerType == containerType);
        }

        public async Task SeedAsync(int listType, int slot, int itemId, int count, byte itemKind)
        {
            var core = InventoryItemCore.Encode(Core(itemId, count, itemKind));
            await using var connection = Open(Path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO items(item_core) VALUES(@core);"
                + "INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) "
                + "VALUES('character',60,@list,@slot,'template',@item,"
                + "last_insert_rowid(),@count)";
            command.Parameters.AddWithValue("@list", listType);
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@core", core);
            command.Parameters.AddWithValue("@item", itemId);
            command.Parameters.AddWithValue("@count", count);
            await command.ExecuteNonQueryAsync();
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(Path); }
            catch (IOException) { }
            return ValueTask.CompletedTask;
        }

        private static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            connection.Open();
            return connection;
        }
    }
}
