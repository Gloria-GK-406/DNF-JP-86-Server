using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Characters;
using Dfo.Server.Infrastructure.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// A container that keeps nothing, answering the same as the one that keeps everything.
/// </summary>
/// <remarks>
/// This is what lets a dungeon hand the player a different inventory without a second copy of the
/// rules: the same script of reads, moves and consumptions runs against both, and the two agree
/// step for step. Where they must differ is storage -- what the transient container holds never
/// reaches the database.
/// </remarks>
public sealed class ItemContainerEquivalenceTests
{
    private const int ItemId = 8474;
    private const int OtherItemId = 9001;
    private const int TransientOnlyItemId = 9999;

    [Fact]
    public async Task The_two_containers_answer_the_same_script_the_same_way()
    {
        await using var storage = await SqliteStorage.CreateAsync();
        await storage.SeedAsync(slot: 65, itemId: ItemId, count: 5);
        await storage.SeedAsync(slot: 66, itemId: OtherItemId, count: 3);

        var shape = new ContainerShape(InventoryContainerType.Main, 0);
        var persistent = new PersistentItemContainer(
            new ContainerId("character:60:Main"), new CharacterId(60), shape, storage.Repository);
        var transient = (TransientItemContainer)new ItemContainerFactory()
            .CreateTransient(new ContainerId("dungeon"), persistent.Shape);
        transient.Place(slotIndex: 65, itemKind: 2, itemId: ItemId, count: 5);
        transient.Place(slotIndex: 66, itemKind: 2, itemId: OtherItemId, count: 3);

        var fromPersistent = await RunScriptAsync(persistent);
        var fromTransient = await RunScriptAsync(transient);

        Assert.Equal(fromPersistent, fromTransient);
    }

    [Fact]
    public async Task What_the_transient_container_holds_never_reaches_storage()
    {
        await using var storage = await SqliteStorage.CreateAsync();
        await storage.SeedAsync(slot: 65, itemId: ItemId, count: 5);
        var transient = (TransientItemContainer)new ItemContainerFactory().CreateTransient(
            new ContainerId("dungeon"),
            new ContainerShape(InventoryContainerType.Main, 0));

        transient.Place(slotIndex: 65, itemKind: 2, itemId: ItemId, count: 5);
        transient.Place(slotIndex: 66, itemKind: 2, itemId: OtherItemId, count: 3);
        transient.Place(slotIndex: 121, itemKind: 2, itemId: TransientOnlyItemId, count: 7);
        _ = await RunScriptAsync(transient);

        Assert.DoesNotContain(
            await storage.ReadAllAsync(), row => row.ItemId == TransientOnlyItemId);
        Assert.Equal([(65, ItemId, 5)], await storage.ReadAllAsync());
    }

    /// <summary>
    /// Every step a served protocol can take, and the refusals as well.
    /// </summary>
    /// <remarks>
    /// Settling in a slot, splitting a pile, pouring one pile into another, trading two slots, and
    /// taking one off -- plus the two ways a decided move is refused, because a container that
    /// accepted what storage rejects would let a stale drag through.
    /// </remarks>
    private static async Task<List<string>> RunScriptAsync(IItemContainer container)
    {
        var observations = new List<string>();
        var token = CancellationToken.None;

        var start = await container.ReadSlotAsync(65, token);
        observations.Add(Describe("start", start));

        Assert.True(await container.ApplyMoveAsync(
            new ContainerMovePlan(start!.StorageEntryId, 3, start.Count, null,
                ContainerMoveKind.Relocate), token));
        observations.Add(Describe("moved", await container.ReadSlotAsync(3, token)));
        observations.Add(Describe("vacated", await container.ReadSlotAsync(65, token)));

        var whole = await container.ReadSlotAsync(3, token);
        Assert.True(await container.ApplyMoveAsync(
            new ContainerMovePlan(whole!.StorageEntryId, 67, 2, null, ContainerMoveKind.Split),
            token));
        observations.Add(Describe("kept", await container.ReadSlotAsync(3, token)));
        observations.Add(Describe("split", await container.ReadSlotAsync(67, token)));

        // Two occupied slots holding different items trade places.
        var left = await container.ReadSlotAsync(67, token);
        var right = await container.ReadSlotAsync(66, token);
        Assert.True(await container.ApplyMoveAsync(
            new ContainerMovePlan(
                left!.StorageEntryId, right!.SlotIndex, left.Count, right.StorageEntryId,
                ContainerMoveKind.Swap),
            token));
        observations.Add(Describe("swapped", await container.ReadSlotAsync(66, token)));
        observations.Add(Describe("traded", await container.ReadSlotAsync(67, token)));

        // A decision taken against a slot that has since changed, and one that asks for more than
        // the pile holds. Both are ordinary refusals rather than faults.
        var stale = await container.ReadSlotAsync(66, token);
        var staleRefused = await container.ApplyMoveAsync(
            new ContainerMovePlan(
                stale!.StorageEntryId, 3, stale.Count, null, ContainerMoveKind.Relocate),
            token);
        var overdrawnRefused = await container.ApplyMoveAsync(
            new ContainerMovePlan(
                stale.StorageEntryId, 68, stale.Count + 1, null, ContainerMoveKind.Relocate),
            token);
        var selfRefused = await container.ApplyMoveAsync(
            new ContainerMovePlan(
                stale.StorageEntryId, stale.SlotIndex, stale.Count, stale.StorageEntryId,
                ContainerMoveKind.Merge),
            token);
        observations.Add(
            $"stale {staleRefused} overdrawn {overdrawnRefused} self {selfRefused}");
        observations.Add(Describe("unmoved", await container.ReadSlotAsync(66, token)));

        var part = await container.ReadSlotAsync(66, token);
        Assert.True(await container.ApplyMoveAsync(
            new ContainerMovePlan(
                part!.StorageEntryId, 3, part.Count,
                (await container.ReadSlotAsync(3, token))!.StorageEntryId,
                ContainerMoveKind.Merge),
            token));
        observations.Add(Describe("merged", await container.ReadSlotAsync(3, token)));
        observations.Add(Describe("emptied", await container.ReadSlotAsync(66, token)));

        var pile = await container.ReadSlotAsync(3, token);
        var consumed = await container.ConsumeAsync(pile!.StorageEntryId, 1, token);
        observations.Add($"consumed {consumed!.SlotIndex} {consumed.ItemId} {consumed.RemainingCount}");

        observations.AddRange(
            (await container.SnapshotAsync(token)).Select(item =>
                $"final {item.SlotIndex} {item.Core.ItemKind} {item.Core.ItemId} {item.Core.Value}"));
        return observations;
    }

    private static string Describe(string label, ContainerSlotItem? item) =>
        item is null
            ? $"{label} empty"
            : $"{label} {item.SlotIndex} {item.ItemKind} {item.ItemId} {item.Count}";

    private sealed class SqliteStorage : IAsyncDisposable
    {
        private SqliteStorage(string path, SqliteEntryInventoryMutationRepository repository)
        {
            Path = path;
            Repository = repository;
        }

        public string Path { get; }

        public SqliteEntryInventoryMutationRepository Repository { get; }

        public static async Task<SqliteStorage> CreateAsync()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-container-{Guid.NewGuid():N}.db");
            _ = new SqliteInventoryEntryRepository(path);
            _ = new SqliteCharacterRepository(path);
            var storage = new SqliteStorage(path, new SqliteEntryInventoryMutationRepository(path));
            await using var connection = Open(path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'container')";
            await command.ExecuteNonQueryAsync();
            return storage;
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
