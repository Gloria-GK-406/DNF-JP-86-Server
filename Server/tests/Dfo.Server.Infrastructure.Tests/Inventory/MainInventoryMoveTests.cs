using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Infrastructure.Characters;
using Dfo.Server.Infrastructure.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Tests.Inventory;

/// <summary>
/// Moving an item between two slots of the main inventory.
/// </summary>
public sealed class MainInventoryMoveTests
{
    private static readonly CharacterId Character = new(60);

    [Fact]
    public async Task An_item_relocates_to_an_empty_slot()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(65, itemId: 8474, count: 1);
        var slots = await fixture.Repository.ReadMainSlotsAsync(Character, 3, 65, CancellationToken.None);

        Assert.Null(slots.Source);
        var moving = Assert.IsType<MainInventoryOccupant>(slots.Destination);
        Assert.True(await fixture.Repository.ApplyMainMoveAsync(
            Character,
            new MainInventoryMovePlan(moving.StorageEntryId, 3, 1, null, MainInventoryMoveKind.Relocate),
            CancellationToken.None));

        Assert.Equal([(3, 8474, 1)], await fixture.ReadAllAsync());
    }

    /// <remarks>
    /// The slot is part of a unique key, so a swap cannot write both rows straight across. What
    /// matters to the caller is that neither row is lost and neither is left on the parking slot
    /// the transaction uses in between.
    /// </remarks>
    [Fact]
    public async Task Two_items_trade_places()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(3, itemId: 100, count: 1);
        await fixture.SeedAsync(65, itemId: 8474, count: 1);
        var slots = await fixture.Repository.ReadMainSlotsAsync(Character, 65, 3, CancellationToken.None);

        Assert.True(await fixture.Repository.ApplyMainMoveAsync(
            Character,
            new MainInventoryMovePlan(
                slots.Source!.StorageEntryId, 3, 1, slots.Destination!.StorageEntryId,
                MainInventoryMoveKind.Swap),
            CancellationToken.None));

        Assert.Equal([(3, 8474, 1), (65, 100, 1)], await fixture.ReadAllAsync());
    }

    [Fact]
    public async Task A_whole_pile_poured_onto_its_own_kind_leaves_one_stack()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(65, itemId: 8474, count: 3);
        await fixture.SeedAsync(66, itemId: 8474, count: 4);
        var slots = await fixture.Repository.ReadMainSlotsAsync(Character, 65, 66, CancellationToken.None);

        Assert.True(await fixture.Repository.ApplyMainMoveAsync(
            Character,
            new MainInventoryMovePlan(
                slots.Source!.StorageEntryId, 66, 3, slots.Destination!.StorageEntryId,
                MainInventoryMoveKind.Merge),
            CancellationToken.None));

        Assert.Equal([(66, 8474, 7)], await fixture.ReadAllAsync());
    }

    [Fact]
    public async Task Part_of_a_pile_moved_to_an_empty_slot_leaves_the_rest_behind()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(65, itemId: 8474, count: 5);
        var slots = await fixture.Repository.ReadMainSlotsAsync(Character, 65, 3, CancellationToken.None);

        Assert.True(await fixture.Repository.ApplyMainMoveAsync(
            Character,
            new MainInventoryMovePlan(
                slots.Source!.StorageEntryId, 3, 2, null, MainInventoryMoveKind.Split),
            CancellationToken.None));

        Assert.Equal([(3, 8474, 2), (65, 8474, 3)], await fixture.ReadAllAsync());
    }

    /// <remarks>
    /// The caller decides the move from a read taken outside the transaction. A target slot that
    /// filled up in between is answered with false and nothing is written, so the client is told
    /// the move did not happen rather than having it forced through.
    /// </remarks>
    [Fact]
    public async Task A_target_slot_that_changed_under_the_decision_refuses_the_move()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(65, itemId: 8474, count: 1);
        var slots = await fixture.Repository.ReadMainSlotsAsync(Character, 65, 3, CancellationToken.None);
        await fixture.SeedAsync(3, itemId: 100, count: 1);

        Assert.False(await fixture.Repository.ApplyMainMoveAsync(
            Character,
            new MainInventoryMovePlan(
                slots.Source!.StorageEntryId, 3, 1, null, MainInventoryMoveKind.Relocate),
            CancellationToken.None));

        Assert.Equal([(3, 100, 1), (65, 8474, 1)], await fixture.ReadAllAsync());
    }

    [Fact]
    public async Task A_move_of_more_than_the_stack_holds_is_refused()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(65, itemId: 8474, count: 1);
        var slots = await fixture.Repository.ReadMainSlotsAsync(Character, 65, 3, CancellationToken.None);

        Assert.False(await fixture.Repository.ApplyMainMoveAsync(
            Character,
            new MainInventoryMovePlan(
                slots.Source!.StorageEntryId, 3, 2, null, MainInventoryMoveKind.Relocate),
            CancellationToken.None));

        Assert.Equal([(65, 8474, 1)], await fixture.ReadAllAsync());
    }

    [Fact]
    public async Task Consuming_one_leaves_the_rest_of_the_pile()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(3, itemId: 8474, count: 2);
        var item = await fixture.Repository.ReadMainSlotAsync(Character, 3, CancellationToken.None);

        var consumed = await fixture.Repository.ConsumeMainItemAsync(
            Character, item!.StorageEntryId, 1, CancellationToken.None);

        Assert.Equal(new ConsumedInventoryItem(3, 8474, 1), consumed);
        Assert.Equal([(3, 8474, 1)], await fixture.ReadAllAsync());
    }

    /// <remarks>
    /// An empty pile left behind would be a slot the client draws as occupied and the server
    /// answers as empty.
    /// </remarks>
    [Fact]
    public async Task Consuming_the_last_one_removes_the_row()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(3, itemId: 8474, count: 1);
        var item = await fixture.Repository.ReadMainSlotAsync(Character, 3, CancellationToken.None);

        var consumed = await fixture.Repository.ConsumeMainItemAsync(
            Character, item!.StorageEntryId, 1, CancellationToken.None);

        Assert.Equal(0, consumed!.RemainingCount);
        Assert.Empty(await fixture.ReadAllAsync());
    }

    [Fact]
    public async Task Consuming_more_than_the_pile_holds_changes_nothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(3, itemId: 8474, count: 1);
        var item = await fixture.Repository.ReadMainSlotAsync(Character, 3, CancellationToken.None);

        Assert.Null(await fixture.Repository.ConsumeMainItemAsync(
            Character, item!.StorageEntryId, 2, CancellationToken.None));
        Assert.Equal([(3, 8474, 1)], await fixture.ReadAllAsync());
    }

    [Fact]
    public async Task An_empty_slot_reads_as_nothing()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Null(await fixture.Repository.ReadMainSlotAsync(Character, 3, CancellationToken.None));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string path, SqliteEntryInventoryMutationRepository repository)
        {
            Path = path;
            Repository = repository;
        }

        public string Path { get; }

        public SqliteEntryInventoryMutationRepository Repository { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-move-{Guid.NewGuid():N}.db");
            _ = new SqliteInventoryEntryRepository(path);
            _ = new SqliteCharacterRepository(path);
            var fixture = new Fixture(path, new SqliteEntryInventoryMutationRepository(path));
            await using var connection = Open(path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'move')";
            await command.ExecuteNonQueryAsync();
            return fixture;
        }

        public async Task SeedAsync(int slot, int itemId, int count)
        {
            var core = new byte[82];
            core[0] = 2;
            core[66] = byte.MaxValue;
            BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1, 4), itemId);
            BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5, 4), count);
            await using var connection = Open(Path);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,slot_index,item_core) VALUES('character',60,60,0,@slot,@core)";
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@core", core);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<List<(int Slot, int ItemId, int Count)>> ReadAllAsync()
        {
            var rows = new List<(int, int, int)>();
            await using var connection = Open(Path);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT slot_index,item_core FROM character_new_items WHERE character_id=60 AND list_type=0 ORDER BY slot_index";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var core = (byte[])reader.GetValue(1);
                rows.Add((
                    reader.GetInt32(0),
                    BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1, 4)),
                    BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(5, 4))));
            }
            return rows;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a green test over.
            }
            return ValueTask.CompletedTask;
        }

        private static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            return connection;
        }
    }
}
