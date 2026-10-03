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

/// <summary>
/// The inventory the client is told about, taken from the container instead of from the loader.
/// </summary>
/// <remarks>
/// Two readers of the same rows: the entry loader, which is where the projection's content came
/// from before, and the container, which is where it comes from once a dungeon can replace it.
/// Changing the source is only safe if the frame does not move, so this compares encoded bytes
/// rather than objects -- the container is a new road to the same place, and the client must not be
/// able to tell which road was taken.
///
/// The rows are seeded out of slot order, and one of them sits in another list, so a container that
/// read them in a different order or forgot which list it was would part company with the loader
/// here rather than in front of a player.
/// </remarks>
public sealed class ContainerInventoryProjectionTests
{
    private static readonly DateTimeOffset PreparedAt =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly AccountId Account = new(6);

    private static readonly CharacterId Character = new(60);

    [Fact]
    public async Task A_container_supplied_snapshot_encodes_to_the_bytes_the_loader_produces()
    {
        await using var storage = await SqliteStorage.CreateAsync();
        await storage.SeedAsync(listType: 0, slot: 121, itemId: 3001, count: 1, itemKind: 3);
        await storage.SeedAsync(listType: 0, slot: 3, itemId: 8474, count: 2, itemKind: 2);
        await storage.SeedAsync(listType: 0, slot: 65, itemId: 8475, count: 99, itemKind: 2);
        await storage.SeedAsync(listType: 2, slot: 0, itemId: 7777, count: 4, itemKind: 2);

        var fromLoader = await storage.LoadContainerAsync(InventoryContainerType.Main);
        var container = storage.OpenContainer(fromLoader.Parameter);

        var containerState = new InventoryContainerState(
            container.Type,
            container.Shape.Parameter,
            await container.SnapshotAsync(CancellationToken.None));

        Assert.Equal(Encode(fromLoader), Encode(containerState));
        Assert.Equal(3, containerState.Items.Count);
    }

    [Fact]
    public async Task A_container_supplied_delta_encodes_to_the_bytes_the_loader_produces()
    {
        await using var storage = await SqliteStorage.CreateAsync();
        await storage.SeedAsync(listType: 0, slot: 65, itemId: 8475, count: 99, itemKind: 2);
        await storage.SeedAsync(listType: 0, slot: 3, itemId: 8474, count: 2, itemKind: 2);

        var fromLoader = await storage.LoadContainerAsync(InventoryContainerType.Main);
        var container = storage.OpenContainer(fromLoader.Parameter);
        var moved = Assert.Single(await container.SnapshotAsync(CancellationToken.None), item => item.SlotIndex == 3);

        var fromContainer = new InventoryDeltaMessage(
            container.Type,
            [.. (await container.SnapshotAsync(CancellationToken.None))
                .Where(item => item.SlotIndex == moved!.SlotIndex)],
            PreparedAt).EncodeBody();
        var expected = new InventoryDeltaMessage(
            InventoryContainerType.Main,
            [.. fromLoader.Items.Where(item => item.SlotIndex == 3)],
            PreparedAt).EncodeBody();

        Assert.Equal(expected, fromContainer);
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
            Mutations = side.MutationsPerCall;
        }

        public string Path { get; }

        private ICharacterInventoryEntryRepository Entries { get; }

        private IEntryInventoryMutationRepository Mutations { get; }

        public static async Task<SqliteStorage> CreateAsync()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-projection-{Guid.NewGuid():N}.db");
            _ = new SqliteDatabase(path, new DomainTransactionScope());
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
                Character,
                new ContainerShape(InventoryContainerType.Main, parameter),
                new SqliteItemDomainStore(Path),
                new DomainTransactionScope(),
                Mutations,
                // A read now asks the archive what sort of thing each slot holds, the same
                // question a write of that slot asks, so a rules service is supplied rather than
                // left null. Nothing here is declared, which is the answer an undeclared template
                // gets anyway: an ordinary pile.
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
            var loaded = await new CharacterInventoryEntryDomainService(Entries)
                .LoadAsync(Account, Character, CancellationToken.None);
            return loaded.Containers.Single(value => value.ContainerType == containerType);
        }

        public async Task SeedAsync(int listType, int slot, int itemId, int count, byte itemKind)
        {
            var core = new byte[82];
            core[0] = itemKind;
            core[66] = byte.MaxValue;
            BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1, 4), itemId);
            BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5, 4), count);
            BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(21, 4), -1);
            await using var connection = Open(Path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO items(item_core) VALUES(@core);"
                + "INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) "
                + "VALUES('character',60,@list,@slot,'world',last_insert_rowid(),"
                + "last_insert_rowid(),1)";
            command.Parameters.AddWithValue("@list", listType);
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@core", core);
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
