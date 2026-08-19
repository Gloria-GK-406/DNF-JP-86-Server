using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
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
///
/// The script is the same one as before, said in the vocabulary that replaced the move plan. There
/// is now a third thing it proves by construction: both carriers run the *same code* for each
/// operation, because the operations are no longer implemented once per carrier.
/// </remarks>
public sealed class ItemContainerCarrierEquivalenceTests
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
        var scope = new DomainTransactionScope();
        var persistent = new PersistentItemContainer(
            ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Main),
            new CharacterId(60), shape, new SqliteItemStore(storage.Path), scope, storage.Repository);
        var transient = new ItemContainerFactory(new MemoryItemStore(), scope)
            .CreateTransient(new ContainerId("dungeon"), persistent.Shape);
        await FillAsync(transient, (65, ItemId, 5), (66, OtherItemId, 3));

        var fromPersistent = await RunScriptAsync(persistent, scope);
        var fromTransient = await RunScriptAsync(transient, scope);

        Assert.Equal(fromPersistent, fromTransient);
    }

    [Fact]
    public async Task What_the_transient_container_holds_never_reaches_storage()
    {
        await using var storage = await SqliteStorage.CreateAsync();
        await storage.SeedAsync(slot: 65, itemId: ItemId, count: 5);
        var scope = new DomainTransactionScope();
        var transient = new ItemContainerFactory(new MemoryItemStore(), scope).CreateTransient(
            new ContainerId("dungeon"),
            new ContainerShape(InventoryContainerType.Main, 0));

        await FillAsync(
            transient, (65, ItemId, 5), (66, OtherItemId, 3), (121, TransientOnlyItemId, 7));
        _ = await RunScriptAsync(transient, scope);

        Assert.DoesNotContain(
            await storage.ReadAllAsync(), row => row.ItemId == TransientOnlyItemId);
        Assert.Equal([(65, ItemId, 5)], await storage.ReadAllAsync());
    }

    /// <summary>
    /// Every step a served protocol can take.
    /// </summary>
    /// <remarks>
    /// Settling in a slot, splitting a pile, pouring one pile into another, trading two slots, and
    /// taking one off. Each runs inside a use case boundary, because the container that keeps its
    /// items refuses a change without one -- which is itself part of what the two carriers must
    /// agree about, and is asserted separately in
    /// <see cref="ItemContainerOutsideTransactionTests"/>.
    /// </remarks>
    private static async Task<List<string>> RunScriptAsync(
        IItemContainer container, DomainTransactionScope scope)
    {
        var observations = new List<string>();
        var token = CancellationToken.None;

        observations.Add(Describe("start", await ReadAsync(container, 65)));

        await InTransactionAsync(scope, async () =>
        {
            var start = await container.ReadSlotAsync(65, token);
            await ItemContainerOperations.RelocateAsync(container, start!, 3, token);
        });
        observations.Add(Describe("moved", await ReadAsync(container, 3)));
        observations.Add(Describe("vacated", await ReadAsync(container, 65)));

        await InTransactionAsync(scope, async () =>
        {
            var whole = await container.ReadSlotAsync(3, token);
            await ItemContainerOperations.SplitAsync(container, whole!, 67, 2, token);
        });
        observations.Add(Describe("kept", await ReadAsync(container, 3)));
        observations.Add(Describe("split", await ReadAsync(container, 67)));

        // Two occupied slots holding different items trade places.
        await InTransactionAsync(scope, async () =>
        {
            var left = await container.ReadSlotAsync(67, token);
            var right = await container.ReadSlotAsync(66, token);
            await ItemContainerOperations.SwapAsync(container, left!, right!, token);
        });
        observations.Add(Describe("swapped", await ReadAsync(container, 66)));
        observations.Add(Describe("traded", await ReadAsync(container, 67)));

        await InTransactionAsync(scope, async () =>
        {
            var part = await container.ReadSlotAsync(66, token);
            var into = await container.ReadSlotAsync(3, token);
            await ItemContainerOperations.MergeAsync(container, part!, into!, part!.Count, token);
        });
        observations.Add(Describe("merged", await ReadAsync(container, 3)));
        observations.Add(Describe("emptied", await ReadAsync(container, 66)));

        var remaining = 0;
        await InTransactionAsync(scope, async () =>
        {
            var pile = await container.ReadSlotAsync(3, token);
            remaining = await ItemContainerOperations.ConsumeAsync(container, pile!, 1, token);
        });
        var consumed = (await ReadAsync(container, 3))!;
        observations.Add($"consumed {consumed.SlotIndex} {consumed.ItemId} {remaining}");

        observations.AddRange(
            (await SnapshotAsync(container)).Select(item =>
                $"final {item.SlotIndex} {item.ItemKind} {item.ItemId} {item.Count}"));
        return observations;
    }

    /// <summary>Fills a container before it is used, which is a write like any other.</summary>
    private static async Task FillAsync(
        IItemContainer container, params (int Slot, int ItemId, int Count)[] placed)
    {
        foreach (var (slot, itemId, count) in placed)
        {
            await container.PutAsync(
                slot, StackableItemCore.Create(2, itemId, count), CancellationToken.None);
        }
    }

    /// <summary>
    /// Runs one use case, so that what it writes settles the way a request's writes settle.
    /// </summary>
    private static async Task InTransactionAsync(DomainTransactionScope scope, Func<Task> useCase)
    {
        await using var transaction = scope.Begin();
        await useCase();
        await transaction.CommitAsync(CancellationToken.None);
    }

    private static async Task<ContainerSlotItem?> ReadAsync(IItemContainer container, int slot) =>
        await container.ReadSlotAsync(slot, CancellationToken.None);

    /// <summary>
    /// Every occupied slot, read the one way both carriers answer alike.
    /// </summary>
    /// <remarks>
    /// Not <c>SnapshotAsync</c>: the persistent container answers that from the entry loader, which
    /// is a second reader of the same rows and not the container's own contents. Comparing the two
    /// carriers on it would compare the loader against a store.
    /// </remarks>
    private static async Task<List<ContainerSlotItem>> SnapshotAsync(IItemContainer container)
    {
        var occupied = new List<ContainerSlotItem>();
        for (var slot = 0; slot <= 130; slot++)
        {
            if (await container.ReadSlotAsync(slot, CancellationToken.None) is { } item)
                occupied.Add(item);
        }

        return occupied;
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
            var storage = new SqliteStorage(path, new SqliteEntryInventoryMutationRepository(
                path, new DailyRefillRuleService(), new ItemArrivalRuleService()));
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
            BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(21, 4), -1);
            await using var connection = Open(Path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,"
                + "slot_index,item_core) VALUES('character',60,60,0,@slot,@core)";
            command.Parameters.AddWithValue("@slot", slot);
            command.Parameters.AddWithValue("@core", core);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<List<(int Slot, int ItemId, int Count)>> ReadAllAsync()
        {
            await using var connection = Open(Path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT slot_index,item_core FROM character_new_items "
                + "WHERE character_id=60 AND list_type=0 ORDER BY slot_index";
            var rows = new List<(int, int, int)>();
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

        private static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            connection.Open();
            return connection;
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
                // A file the runtime still holds is left for the temp directory to reap.
            }

            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// What each carrier does with a change made outside a use case boundary.
/// </summary>
/// <remarks>
/// This is the difference the two carriers are allowed to have, and the only one. A container whose
/// items outlive the process refuses, because such a change could not be taken back and nobody
/// would ever see that it had happened. A container whose items do not has nothing to take back.
/// </remarks>
public sealed class ItemContainerOutsideTransactionTests
{
    private static readonly ContainerId Main =
        ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Main);

    [Fact]
    public async Task ChangingThePersistentContainerOutsideATransactionThrows()
    {
        var container = Persistent(new DomainTransactionScope());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await container.PutAsync(3, Core(1), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await container.ClearAsync(3, CancellationToken.None));
    }

    [Fact]
    public async Task ChangingTheTransientContainerOutsideATransactionDoesNotThrow()
    {
        var container = Transient(new DomainTransactionScope());

        await container.PutAsync(3, Core(1), CancellationToken.None);

        Assert.Equal(1, (await container.ReadSlotAsync(3, CancellationToken.None))!.Count);
        await container.ClearAsync(3, CancellationToken.None);
        Assert.Null(await container.ReadSlotAsync(3, CancellationToken.None));
    }

    /// <remarks>
    /// Reading is not changing. A container is looked at in places that are not use cases at all,
    /// and refusing that would make the boundary a rule about looking rather than about writing.
    /// </remarks>
    [Fact]
    public async Task ReadingThePersistentContainerOutsideATransactionIsAllowed()
    {
        var container = Persistent(new DomainTransactionScope());

        Assert.Null(await container.ReadSlotAsync(3, CancellationToken.None));
    }

    [Fact]
    public async Task InsideATransactionThePersistentContainerAccepts()
    {
        var scope = new DomainTransactionScope();
        var container = Persistent(scope);

        await using (var transaction = scope.Begin())
        {
            await container.PutAsync(3, Core(2), CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        Assert.Equal(2, (await container.ReadSlotAsync(3, CancellationToken.None))!.Count);
    }

    private static IItemContainer Persistent(DomainTransactionScope scope)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-boundary-{Guid.NewGuid():N}.db");
        _ = new SqliteInventoryEntryRepository(path);
        _ = new SqliteCharacterRepository(path);
        return new PersistentItemContainer(
            Main,
            new CharacterId(60),
            new ContainerShape(InventoryContainerType.Main, 0),
            new SqliteItemStore(path),
            scope,
            null!);
    }

    private static IItemContainer Transient(DomainTransactionScope scope) =>
        new ItemContainerFactory(new MemoryItemStore(), scope).CreateTransient(
            new ContainerId("dungeon"), new ContainerShape(InventoryContainerType.Main, 0));

    private static InventoryItemCoreState Core(int count) =>
        StackableItemCore.Create(2, 8474, count);
}
