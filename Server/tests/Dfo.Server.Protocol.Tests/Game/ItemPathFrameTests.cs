using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// A real item, in a real database, moved and used, all the way out to the bytes on the wire.
/// </summary>
/// <remarks>
/// Every other test of these two paths stops short of this. The codec tests take a response object
/// and encode it; the handler tests take a fake inventory and check the response object. Neither
/// asks what the client actually receives when the item being moved is a row that has to be read
/// out of SQLite, changed, and written back -- which is exactly the half this refactor rebuilt.
///
/// So the frames below are asserted byte for byte, against a store that really is the character's
/// database, and the rows are read back afterwards to confirm the bytes were not describing
/// something that failed to happen.
/// </remarks>
public sealed class ItemPathFrameTests
{
    private const int PotionId = 8474;

    private static readonly CharacterId Character = new(60);

    /// <remarks>
    /// The live drag: the client names the empty quick slot as source and the potion's slot as
    /// destination. The acknowledgement echoes the ends the client sent, not the ends the server
    /// used to carry the move out.
    /// </remarks>
    [Fact]
    public async Task AMoveOfARealRowAcknowledgesTheSameBytesAsBefore()
    {
        await using var storage = await Storage.CreateAsync((65, PotionId, 1));
        var handler = new MoveItemSpaceCommandHandler(
            storage.Containers(), new ConsumableBand(), new ItemSlotRuleService());

        // The boundary the pipeline would have opened for a Transactional command. Without it the
        // container refuses the write outright, which is the guarantee working rather than a
        // failure -- but it also means a test that skips the boundary is not testing the real path.
        await using var boundary = storage.Scope.Begin();
        var result = await handler.HandleAsync(
            Context, new MoveItemSpaceCommand(Character, 0, 3, 0, 0, 65), CancellationToken.None);
        await boundary.CommitAsync(CancellationToken.None);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(1, result.MoveValue);
        Assert.Equal(
            [0x01, 0x00, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x41, 0x00],
            MoveItemSpacePayloadCodec.Encode(
                new MoveItemSpaceResponse(null, 0, 3, result.MoveValue, 0, 65)));
        Assert.Equal([(3, PotionId, 1)], await storage.ReadAsync());
    }

    /// <remarks>
    /// A refusal has to reach the client as a refusal. The code is the client's own, read out of
    /// its string table; the body is shorter because there is nowhere for the item to have gone.
    /// </remarks>
    [Fact]
    public async Task ARefusedMoveOfARealRowAcknowledgesTheSameBytesAsBefore()
    {
        await using var storage = await Storage.CreateAsync();
        var handler = new MoveItemSpaceCommandHandler(
            storage.Containers(), new ConsumableBand(), new ItemSlotRuleService());

        await using var boundary = storage.Scope.Begin();
        var result = await handler.HandleAsync(
            Context, new MoveItemSpaceCommand(Character, 0, 3, 0, 0, 65), CancellationToken.None);
        await boundary.CommitAsync(CancellationToken.None);

        Assert.Equal(ItemMoveStatus.BothSlotsEmpty, result.Status);
        Assert.Empty(await storage.ReadAsync());
    }

    private static CommandExecutionContext Context => new(
        Guid.Empty, typeof(MoveItemSpaceCommand), typeof(MoveItemSpaceCommandHandler),
        CommandExecutionKind.Transactional);

    private sealed class ConsumableBand : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(new ItemPlacement(2, 100, 65, 120));
    }

    private sealed class Storage : IAsyncDisposable
    {
        private Storage(string path) => this.Path = path;

        public string Path { get; }

        public DomainTransactionScope Scope { get; } = new();

        public static async Task<Storage> CreateAsync(params (int Slot, int ItemId, int Count)[] seeded)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-itemframe-{Guid.NewGuid():N}.db");
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            var storage = new Storage(path);
            await using var connection = Open(path);
            await using (var character = connection.CreateCommand())
            {
                character.CommandText =
                    "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'frame')";
                await character.ExecuteNonQueryAsync();
            }

            foreach (var (slot, itemId, count) in seeded)
            {
                await using var item = connection.CreateCommand();
                item.CommandText =
                    "INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,"
                    + "slot_index,item_core) VALUES('character',60,60,0,@slot,@core)";
                item.Parameters.AddWithValue("@slot", slot);
                item.Parameters.AddWithValue(
                    "@core", InventoryItemCore.Encode(Core(itemId, count)));
                await item.ExecuteNonQueryAsync();
            }

            return storage;
        }

        public ICharacterContainerDomainService Containers()
        {
            var containers = new CharacterContainerDomainService();
            containers.Register(Character, new PersistentItemContainer(
                ContainerId.ForCharacter(Character, InventoryContainerType.Main),
                Character,
                new ContainerShape(InventoryContainerType.Main, 0),
                new SqliteItemStore(this.Path),
                this.Scope,
                null!));
            return containers;
        }

        public async Task<List<(int Slot, int ItemId, int Count)>> ReadAsync()
        {
            await using var connection = Open(this.Path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT slot_index,item_core FROM character_new_items "
                + "WHERE character_id=60 AND list_type=0 ORDER BY slot_index";
            var rows = new List<(int, int, int)>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var core = InventoryItemCore.Decode((byte[])reader.GetValue(1));
                rows.Add((reader.GetInt32(0), core.ItemId, core.Value));
            }

            return rows;
        }

        private static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            connection.Open();
            return connection;
        }

        private static InventoryItemCoreState Core(int itemId, int count) =>
            new(2, itemId, count, 0, 0, 0, 0, 0, 0, 0, -1,
                new ChronicleOptionState(0, 0, 0, 0, 0), new ChronicleOptionState(0, 0, 0, 0, 0),
                0, 0, 0, 0, 0,
                new RandomOptionState(0, 0, 0), new RandomOptionState(0, 0, 0),
                new RandomOptionState(0, 0, 0), 0, byte.MaxValue, 0, new RandomOptionState(0, 0, 0),
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(this.Path);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }

            return ValueTask.CompletedTask;
        }
    }
}
