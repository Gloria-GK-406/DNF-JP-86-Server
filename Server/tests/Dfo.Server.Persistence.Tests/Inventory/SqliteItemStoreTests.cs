using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// Whether "container and slot" is enough to say everything this server does to an item.
/// </summary>
/// <remarks>
/// Each of the five operations is written out here as the reads, clears and writes a caller would
/// use, with no identifier for the item anywhere. These were written before the store existed:
/// if one of the five could not be said this way, that was the point at which to stop rather than
/// to invent an identity for items to be addressed by.
/// </remarks>
public sealed class SqliteItemStoreTests
{
    private static readonly ContainerId Main =
        ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Main);

    private static readonly ContainerId Equipment =
        ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Equipment);

    [Fact]
    public async Task Relocate_IsExpressedAsAClearAndAWrite()
    {
        await using var store = await NewStoreAsync(
            (65, Potion(5)));
        var transaction = new StubTransaction();
        var session = store.Value.SessionIn(transaction);

        var moving = await session.ReadAsync(Main, 65, CancellationToken.None);
        session.Clear(Main, 65);
        session.Write(Main, new StoredItem(3, moving!.Core));
        await transaction.SettleAsync();

        Assert.Equal([(3, PotionId, 5)], await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task Split_IsExpressedAsTwoWrites()
    {
        await using var store = await NewStoreAsync(
            (65, Potion(5)));
        var transaction = new StubTransaction();
        var session = store.Value.SessionIn(transaction);

        var moving = await session.ReadAsync(Main, 65, CancellationToken.None);
        session.Write(Main, new StoredItem(65, moving!.Core with { Value = 3 }));
        session.Write(Main, new StoredItem(66, Potion(2)));
        await transaction.SettleAsync();

        Assert.Equal([(65, PotionId, 3), (66, PotionId, 2)], await ReadSlotsAsync(store.Path));
    }

    /// <summary>
    /// The swap is the operation that decides the question.
    /// </summary>
    /// <remarks>
    /// Storage addressed by row could not write both ends straight across -- the slot is part of a
    /// unique key, so the first update collided with the row it was about to displace, and the
    /// implementation this replaces parked one row on slot -1 for the length of its transaction to
    /// get around it. Addressed by slot there is nothing to get around: each slot is simply told
    /// what it now holds.
    /// </remarks>
    [Fact]
    public async Task Swap_IsExpressedAsTwoWritesWithNoParkedSlot()
    {
        await using var store = await NewStoreAsync(
            (65, Potion(5)),
            (66, Stone(1)));
        var transaction = new StubTransaction();
        var session = store.Value.SessionIn(transaction);

        var first = await session.ReadAsync(Main, 65, CancellationToken.None);
        var second = await session.ReadAsync(Main, 66, CancellationToken.None);
        session.Write(Main, new StoredItem(66, first!.Core));
        session.Write(Main, new StoredItem(65, second!.Core));
        await transaction.SettleAsync();

        Assert.Equal([(65, StoneId, 1), (66, PotionId, 5)], await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task Merge_IsExpressedAsAWriteAndAClear()
    {
        await using var store = await NewStoreAsync(
            (65, Potion(5)),
            (66, Potion(2)));
        var transaction = new StubTransaction();
        var session = store.Value.SessionIn(transaction);

        var moving = await session.ReadAsync(Main, 65, CancellationToken.None);
        var target = await session.ReadAsync(Main, 66, CancellationToken.None);
        session.Write(Main, new StoredItem(66, target!.Core with { Value = 7 }));
        session.Clear(Main, moving!.SlotIndex);
        await transaction.SettleAsync();

        Assert.Equal([(66, PotionId, 7)], await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task PartialConsume_IsExpressedAsAWrite()
    {
        await using var store = await NewStoreAsync(
            (65, Potion(5)));
        var transaction = new StubTransaction();
        var session = store.Value.SessionIn(transaction);

        var used = await session.ReadAsync(Main, 65, CancellationToken.None);
        session.Write(Main, new StoredItem(65, used!.Core with { Value = 4 }));
        await transaction.SettleAsync();

        Assert.Equal([(65, PotionId, 4)], await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task ConsumingTheLastOfAPile_IsExpressedAsAClear()
    {
        await using var store = await NewStoreAsync(
            (65, Potion(1)));
        var transaction = new StubTransaction();
        var session = store.Value.SessionIn(transaction);

        var used = await session.ReadAsync(Main, 65, CancellationToken.None);
        session.Clear(Main, used!.SlotIndex);
        await transaction.SettleAsync();

        Assert.Empty(await ReadSlotsAsync(store.Path));
    }

    /// <summary>
    /// An avatar is the one item with facts kept in a second table, and moving it keeps them.
    /// </summary>
    /// <remarks>
    /// This is the case that decided whether a store addressed by slot could be lossless. It can,
    /// and not by leaving the facts where they were: they are columns of the row now, and this store
    /// moves an item by replacing its row. So the facts are read back under the avatar UID the core
    /// carries and written on again -- the link travels inside the core exactly as an enchantment
    /// does, and the store is what makes that true rather than a second table.
    /// </remarks>
    [Fact]
    public async Task Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore()
    {
        var detail = new InventoryAvatarDetailState(
            11, 12, Enumerable.Repeat((byte)13, 30).ToArray(), 14, 15, 16);
        await using var store = await NewStoreAsync((9, Avatar()));
        await SeedAvatarDetailAsync(store.Path, AvatarUid, detail);
        var transaction = new StubTransaction();
        var session = store.Value.SessionIn(transaction);

        var moving = await session.ReadAsync(Main, 9, CancellationToken.None);
        session.Clear(Main, 9);
        session.Write(Main, new StoredItem(10, moving!.Core));
        await transaction.SettleAsync();

        Assert.Equal([(10, AvatarItemId, AvatarUid)], await ReadSlotsAsync(store.Path));
        Assert.Equivalent(detail, await ReadAvatarDetailAsync(store.Path, AvatarUid));
    }

    [Fact]
    public async Task Read_SeesThisSessionsOwnUncommittedWrites()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        var transaction = new StubTransaction();
        var session = store.Value.SessionIn(transaction);

        session.Write(Main, new StoredItem(66, Potion(2)));
        session.Clear(Main, 65);

        Assert.Equal(2, (await session.ReadAsync(Main, 66, CancellationToken.None))!.Core.Value);
        Assert.Null(await session.ReadAsync(Main, 65, CancellationToken.None));
        Assert.Equal([66], (await session.ReadAllAsync(Main, CancellationToken.None))
            .Select(item => item.SlotIndex));
        // Nothing has reached the database yet.
        Assert.Equal([(65, PotionId, 5)], await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task Rollback_LeavesTheStoredItemsUntouched()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        var transaction = new StubTransaction();
        var session = store.Value.SessionIn(transaction);

        session.Clear(Main, 65);
        session.Write(Main, new StoredItem(3, Potion(5)));
        await transaction.PrepareAsync();
        await transaction.RollbackAsync();

        Assert.Equal([(65, PotionId, 5)], await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task Session_TakesPartOnceHoweverOftenItsStoreIsAsked()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        var transaction = new StubTransaction();

        var first = store.Value.SessionIn(transaction);
        var second = store.Value.SessionIn(transaction);

        Assert.Same(first, second);
        Assert.Single(transaction.Participants);
    }

    /// <summary>
    /// The store adds no table of its own; it writes the rows the inventory repositories already
    /// create. Asserted by comparing the schema before and after a full read-write-commit cycle.
    /// </summary>
    [Fact]
    public async Task Store_DefinesNoTableOfItsOwn()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        var before = await ReadTableNamesAsync(store.Path);

        var transaction = new StubTransaction();
        var session = store.Value.SessionIn(transaction);
        _ = await session.ReadAllAsync(Main, CancellationToken.None);
        session.Write(Main, new StoredItem(66, Potion(1)));
        await transaction.SettleAsync();

        Assert.Equal(before, await ReadTableNamesAsync(store.Path));
    }

    /// <summary>
    /// This is the store whose commit can still fail, and it is the only one a use case may hold.
    /// </summary>
    /// <remarks>
    /// The two flags below are what the coordinator reads to enforce that. Nothing else in this
    /// file would fail if they were wrong, and a use case would then be quietly non-atomic.
    ///
    /// The participant is read off the transaction rather than by casting the session, because
    /// what joins is the database this store writes to; the session is one facet of it.
    /// </remarks>
    [Fact]
    public async Task Store_IsTheDurableParticipantWhoseCommitCanFail()
    {
        await using var store = await NewStoreAsync();
        var transaction = new StubTransaction();
        _ = store.Value.SessionIn(transaction);

        Assert.True(store.Value.IsDurable);
        Assert.True(Assert.Single(transaction.Participants).CommitCanFail);
    }

    /// <remarks>
    /// One session reaching more than one of a character's containers settles as a single batch.
    /// The five operations above all live in one container, so nothing there would notice a store
    /// that quietly kept a container's writes to itself.
    /// </remarks>
    [Fact]
    public async Task TwoContainersExchangeTheirContentsInOneSettlement()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        await SeedAsync(store.Path, Equipment, 1, Stone(1));
        var transaction = new StubTransaction();
        var session = store.Value.SessionIn(transaction);

        var fromMain = await session.ReadAsync(Main, 65, CancellationToken.None);
        var fromEquipment = await session.ReadAsync(Equipment, 1, CancellationToken.None);
        session.Clear(Main, 65);
        session.Clear(Equipment, 1);
        session.Write(Main, new StoredItem(65, fromEquipment!.Core));
        session.Write(Equipment, new StoredItem(1, fromMain!.Core));
        await transaction.SettleAsync();

        Assert.Equal([(65, StoneId, 1)], await ReadSlotsAsync(store.Path));
        Assert.Equal([(1, PotionId, 5)], await ReadSlotsAsync(store.Path, Equipment));
    }

    [Fact]
    public async Task ClearingASlotThisSessionJustWroteLeavesItEmpty()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        var transaction = new StubTransaction();
        var session = store.Value.SessionIn(transaction);

        session.Write(Main, new StoredItem(66, Potion(2)));
        session.Clear(Main, 66);

        Assert.Null(await session.ReadAsync(Main, 66, CancellationToken.None));
        await transaction.SettleAsync();
        Assert.Equal([(65, PotionId, 5)], await ReadSlotsAsync(store.Path));
    }

    /// <remarks>
    /// A container this store keeps no rows for is refused, rather than read as an empty one. The
    /// caller would otherwise be told a container it cannot reach simply has nothing in it.
    ///
    /// Account cargo is the trap worth naming: it is a container type a character has, but its rows
    /// live in another table keyed by account, so a write placed here would be a row the server
    /// rejects the next time that character enters.
    /// </remarks>
    [Fact]
    public async Task AContainerThisStoreKeepsNoRowsForIsRefused()
    {
        await using var store = await NewStoreAsync();
        var session = store.Value.SessionIn(new StubTransaction());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await session.ReadAllAsync(
                ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.AccountCargo),
                CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() =>
            session.Clear(new ContainerId("run:1:Main"), 0));
    }

    /// <remarks>
    /// Slot -1 is what the implementation this replaces parked a row on to get a swap through. It is
    /// refused outright now, so that value cannot reach a row and be found at the character's next
    /// entry instead.
    /// </remarks>
    [Fact]
    public async Task ANegativeSlotIsRefused()
    {
        await using var store = await NewStoreAsync();
        var session = store.Value.SessionIn(new StubTransaction());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.Write(Main, new StoredItem(-1, Potion(1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Clear(Main, -1));
    }

    /// <remarks>
    /// Carrying the writes out here instead would make the commit a second act of the use case that
    /// can fail; carrying out nothing and saying so would lose items silently. It does neither.
    /// </remarks>
    [Fact]
    public async Task Commit_RefusesToStandInForAPrepareThatNeverRan()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        var transaction = new StubTransaction();
        var session = store.Value.SessionIn(transaction);
        var participant = Assert.Single(transaction.Participants);

        session.Write(Main, new StoredItem(3, Potion(1)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await participant.CommitAsync(CancellationToken.None));
        Assert.Equal([(65, PotionId, 5)], await ReadSlotsAsync(store.Path));
    }

    private const int PotionId = 1000;

    private const int StoneId = 2000;

    private static InventoryItemCoreState Potion(int count) => Core(6, PotionId, count);

    private static InventoryItemCoreState Stone(int count) => Core(6, StoneId, count);

    private const int AvatarItemId = 3000;

    /// <summary>An avatar row carries its detail row's key in the core's value field.</summary>
    private const int AvatarUid = 777;

    private static InventoryItemCoreState Avatar() => Core(8, AvatarItemId, AvatarUid);

    private static InventoryItemCoreState Core(byte itemKind, int itemId, int count) =>
        new(itemKind, itemId, count, 0, 0, 0, 0, 0, 0, 0, -1,
            new ChronicleOptionState(0, 0, 0, 0, 0), new ChronicleOptionState(0, 0, 0, 0, 0),
            0, 0, 0, 0, 0,
            new RandomOptionState(0, 0, 0), new RandomOptionState(0, 0, 0),
            new RandomOptionState(0, 0, 0), 0, byte.MaxValue, 0, new RandomOptionState(0, 0, 0),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static async Task<StoreUnderTest> NewStoreAsync(
        params (int Slot, InventoryItemCoreState Core)[] seeded)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-itemstore-{Guid.NewGuid():N}.db");
        // The repositories own the schema; this store only writes into it.
        _ = new SqliteDatabase(path, new DomainTransactionScope());
        _ = new SqliteDatabase(path, new DomainTransactionScope());
        await using (var connection = Open(path))
        {
            await using var character = connection.CreateCommand();
            character.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'store')";
            await character.ExecuteNonQueryAsync();
            foreach (var (slot, core) in seeded)
                await InsertItemAsync(connection, slot, core);
        }

        return new StoreUnderTest(path, new SqliteItemStore(path));
    }

    private static async Task SeedAsync(
        string path, ContainerId containerId, int slot, InventoryItemCoreState core)
    {
        Assert.True(containerId.TryGetCharacterOwner(out _, out var containerType));
        await using var connection = Open(path);
        await InsertItemAsync(connection, slot, core, (int)containerType);
    }

    private static async Task InsertItemAsync(
        SqliteConnection connection, int slot, InventoryItemCoreState core, int listType = 0)
    {
        var bytes = new byte[82];
        bytes[0] = core.ItemKind;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1, 4), core.ItemId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(5, 4), core.Value);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(21, 4), -1);
        bytes[66] = byte.MaxValue;
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO items(owner_scope,owner_id,list_type,slot_index,item_core) "
            + "VALUES('character',60,@list,@slot,@core)";
        command.Parameters.AddWithValue("@list", listType);
        command.Parameters.AddWithValue("@slot", slot);
        command.Parameters.AddWithValue("@core", bytes);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedAvatarDetailAsync(
        string path, int avatarUid, InventoryAvatarDetailState detail)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        // The facts belong to the row that is the avatar, so they are put on it. There is one.
        command.CommandText =
            "UPDATE items SET avatar_uid=@uid,avatar_expire_date=@expire,avatar_clear_id=@clear,"
            + "avatar_jewel_socket=@jewel,avatar_color1=@color1,avatar_color2=@color2,"
            + "avatar_delete_date=@delete "
            + "WHERE owner_scope='character' AND owner_id=60 AND substr(item_core,1,1)=X'08'";
        command.Parameters.AddWithValue("@uid", avatarUid);
        command.Parameters.AddWithValue("@expire", detail.ExpireDate);
        command.Parameters.AddWithValue("@clear", detail.ClearAvatarId);
        command.Parameters.AddWithValue("@jewel", detail.JewelSocket);
        command.Parameters.AddWithValue("@color1", (int)detail.Color1);
        command.Parameters.AddWithValue("@color2", (int)detail.Color2);
        command.Parameters.AddWithValue("@delete", detail.DeleteDate);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<InventoryAvatarDetailState?> ReadAvatarDetailAsync(
        string path, int avatarUid)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT avatar_expire_date,avatar_clear_id,avatar_jewel_socket,avatar_color1,"
            + "avatar_color2,avatar_delete_date "
            + "FROM items WHERE avatar_uid=@uid";
        command.Parameters.AddWithValue("@uid", avatarUid);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return new InventoryAvatarDetailState(
            reader.GetInt32(0), reader.GetInt32(1), (byte[])reader.GetValue(2),
            (ushort)reader.GetInt32(3), (ushort)reader.GetInt32(4), reader.GetInt32(5));
    }

    private static async Task<List<(int Slot, int ItemId, int Count)>> ReadSlotsAsync(
        string path, ContainerId? containerId = null)
    {
        var listType = 0;
        if (containerId is { } named)
        {
            Assert.True(named.TryGetCharacterOwner(out _, out var containerType));
            listType = (int)containerType;
        }

        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT slot_index,item_core FROM items "
            + "WHERE owner_scope='character' AND owner_id=60 AND list_type=@list "
            + "ORDER BY slot_index";
        command.Parameters.AddWithValue("@list", listType);
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

    private static async Task<List<string>> ReadTableNamesAsync(string path)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        return connection;
    }

    private sealed record StoreUnderTest(string Path, SqliteItemStore Value) : IAsyncDisposable
    {
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

    /// <summary>
    /// Stands in for the coordinator, so what these tests prove is the store's own behaviour
    /// rather than the coordinator's.
    /// </summary>
    private sealed class StubTransaction : IDomainTransaction
    {
        private readonly Dictionary<object, ITransactionParticipant> enlisted = [];

        public IReadOnlyCollection<ITransactionParticipant> Participants => this.enlisted.Values;

        public TParticipant Enlist<TParticipant>(object identity, Func<TParticipant> create)
            where TParticipant : ITransactionParticipant
        {
            if (this.enlisted.TryGetValue(identity, out var existing))
                return (TParticipant)existing;
            var participant = create();
            this.enlisted.Add(identity, participant);
            return participant;
        }

        public async Task PrepareAsync()
        {
            foreach (var participant in this.enlisted.Values)
                await participant.PrepareAsync(CancellationToken.None);
        }

        public async Task RollbackAsync()
        {
            foreach (var participant in this.enlisted.Values)
                await participant.RollbackAsync(CancellationToken.None);
        }

        public async Task SettleAsync()
        {
            await this.PrepareAsync();
            foreach (var participant in this.enlisted.Values)
                await participant.CommitAsync(CancellationToken.None);
        }
    }
}
