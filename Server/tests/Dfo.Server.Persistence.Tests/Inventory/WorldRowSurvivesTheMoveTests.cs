using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// Whether a thing with an identity is still the same thing after it has been moved.
/// </summary>
/// <remarks>
/// The store used to say a move as a delete and an insert, which gave the thing a new row every
/// time it went anywhere. Nothing outside the table was keyed on that row, so nothing noticed --
/// and nothing could have, because there was no identity for anything to hold on to.
///
/// These hold the two facts that replace it. A world row is written once and taken away once: a
/// move lifts it out of its position and puts it down in another, and the identity the column
/// allocated stays the identity. A position is a row of its own, destroyed and rebuilt as freely
/// as ever, because it says where and what and carries nothing else.
///
/// <c>sqlite_sequence</c> is what makes the first of those checkable rather than merely plausible.
/// The identity column is <c>AUTOINCREMENT</c>, so it never hands out a number it has handed out
/// before: an unchanged high-water mark across a move is a statement that no row was inserted, and
/// the same identity found at the new position is a statement that none was deleted either.
/// </remarks>
public sealed class WorldRowSurvivesTheMoveTests
{
    private static readonly CharacterId Owner = new(60);

    private static readonly ContainerId Main =
        ContainerId.ForCharacter(Owner, InventoryContainerType.Main);

    private static readonly ContainerId Equipment =
        ContainerId.ForCharacter(Owner, InventoryContainerType.Equipment);

    /// <summary>
    /// A worn piece moved, put on and taken off again is the same piece each time.
    /// </summary>
    /// <remarks>
    /// Three moves, one of them across containers, and the identity is read back out of the
    /// database after each one rather than remembered from the write. The high-water mark is
    /// asserted alongside it: were the row being replaced, the identity read back would be a
    /// larger number and this would say which of the two had happened.
    /// </remarks>
    [Fact]
    public async Task AWornPieceKeepsOneIdentityThroughAMoveAWearAndATakeOff()
    {
        await using var store = await NewStoreAsync();
        var sword = Sword(enchant: 7);
        await SettleAsync(store, session => session.Write(Main, Worn(65, sword)));

        var identity = await IdentityAtAsync(store.Path, Main, 65);
        var allocated = await HighWaterMarkAsync(store.Path);

        foreach (var (from, fromSlot, to, toSlot) in new[]
                 {
                     (Main, 65, Main, 3),
                     (Main, 3, Equipment, 5),
                     (Equipment, 5, Main, 70),
                 })
        {
            await SettleAsync(store, session =>
            {
                session.Clear(from, fromSlot);
                session.Write(to, Worn(toSlot, sword));
            });

            Assert.Null(await IdentityAtAsync(store.Path, from, fromSlot));
            Assert.Equal(identity, await IdentityAtAsync(store.Path, to, toSlot));
            Assert.Equal(allocated, await HighWaterMarkAsync(store.Path));
            Assert.Equal(
                new SlotRow("world", identity!.Value, 1),
                await SlotAtAsync(store.Path, to, toSlot));
            Assert.Null(await SlotAtAsync(store.Path, from, fromSlot));
        }

        Assert.Equal(sword, await CoreAtAsync(store.Path, Main, 70));
    }

    /// <summary>
    /// A move deletes no world row and builds none, which is what the check asks in so many words.
    /// </summary>
    /// <remarks>
    /// Said as the whole set of identities before and after rather than as one of them, so a move
    /// that quietly rebuilt some other character's row -- or left the old row behind beside the new
    /// one -- would be caught here as well.
    /// </remarks>
    [Fact]
    public async Task AMoveNeitherDeletesNorBuildsAWorldRow()
    {
        await using var store = await NewStoreAsync();
        await SettleAsync(store, session =>
        {
            session.Write(Main, Worn(65, Sword(enchant: 3)));
            session.Write(Main, Worn(66, Ring()));
        });

        var before = await IdentitiesAsync(store.Path);
        var allocated = await HighWaterMarkAsync(store.Path);

        await SettleAsync(store, session =>
        {
            session.Clear(Main, 65);
            session.Write(Equipment, Worn(2, Sword(enchant: 3)));
        });

        Assert.Equal(before, await IdentitiesAsync(store.Path));
        Assert.Equal(allocated, await HighWaterMarkAsync(store.Path));
    }

    /// <summary>
    /// Two worn pieces exchanging positions keep an identity each, and keep the right one.
    /// </summary>
    /// <remarks>
    /// The swap is the case the whole batch is shaped around: each row's destination is the other
    /// row's position, so neither can be written straight across while the other is still standing
    /// there. Both are lifted out before either is put down.
    /// </remarks>
    [Fact]
    public async Task TwoWornPiecesSwappingPositionsEachKeepTheirOwnIdentity()
    {
        await using var store = await NewStoreAsync();
        var sword = Sword(enchant: 4);
        var ring = Ring();
        await SettleAsync(store, session =>
        {
            session.Write(Main, Worn(10, sword));
            session.Write(Main, Worn(11, ring));
        });

        var swordIdentity = await IdentityAtAsync(store.Path, Main, 10);
        var ringIdentity = await IdentityAtAsync(store.Path, Main, 11);
        var allocated = await HighWaterMarkAsync(store.Path);

        await SettleAsync(store, session =>
        {
            session.Write(Main, Worn(11, sword));
            session.Write(Main, Worn(10, ring));
        });

        Assert.Equal(swordIdentity, await IdentityAtAsync(store.Path, Main, 11));
        Assert.Equal(ringIdentity, await IdentityAtAsync(store.Path, Main, 10));
        Assert.Equal(allocated, await HighWaterMarkAsync(store.Path));
        Assert.Equal(sword, await CoreAtAsync(store.Path, Main, 11));
        Assert.Equal(ring, await CoreAtAsync(store.Path, Main, 10));
    }

    /// <summary>
    /// A contract creature taken off into the bag is the same creature, instance state and all.
    /// </summary>
    /// <remarks>
    /// The eight columns are put on the row after it exists, which is what a creature's own state
    /// will be once it lives there. Nothing in the store writes them, and that is the point: the
    /// move must not touch them, and a move that replaced the row would take every one of them
    /// with it.
    /// </remarks>
    [Fact]
    public async Task AContractCreatureTakenOffIntoTheBagIsStillTheSameCreature()
    {
        await using var store = await NewStoreAsync();
        var creature = Core(5, CreatureItemId, 0);
        await SettleAsync(store, session => session.Write(
            Equipment, new StoredItem(24, creature) { WorldClass = ItemWorldClass.Creature }));

        var identity = await IdentityAtAsync(store.Path, Equipment, 24);
        Assert.NotNull(identity);
        await SetCreatureStateAsync(store.Path, identity.Value, progress: 4242);

        await SettleAsync(store, session =>
        {
            session.Clear(Equipment, 24);
            session.Write(
                Main, new StoredItem(80, creature) { WorldClass = ItemWorldClass.Creature });
        });

        Assert.Equal(identity, await IdentityAtAsync(store.Path, Main, 80));
        Assert.Equal(4242, await CreatureStateAsync(store.Path, identity.Value));
    }

    /// <summary>
    /// An avatar's own columns stay on its row, because nothing takes the row away.
    /// </summary>
    /// <remarks>
    /// The store used to read these back and write them on again, because the row they sat on was
    /// about to be deleted. It no longer is, so they are simply never touched -- which is the
    /// difference between state that follows the thing and state that has to be carried after it.
    /// </remarks>
    [Fact]
    public async Task AnAvatarsOwnColumnsStayWhereTheyAreThroughAMove()
    {
        await using var store = await NewStoreAsync();
        var avatar = Core(AvatarKind, AvatarItemId, AvatarUid);
        await SettleAsync(store, session => session.Write(
            Main, new StoredItem(9, avatar) { WorldClass = ItemWorldClass.Avatar }));

        var identity = await IdentityAtAsync(store.Path, Main, 9);
        Assert.NotNull(identity);
        await SetAvatarColourAsync(store.Path, identity.Value, colour: 321);

        await SettleAsync(store, session =>
        {
            session.Clear(Main, 9);
            session.Write(
                Main, new StoredItem(10, avatar) { WorldClass = ItemWorldClass.Avatar });
        });

        Assert.Equal(identity, await IdentityAtAsync(store.Path, Main, 10));
        Assert.Equal(321, await AvatarColourAsync(store.Path, identity.Value));
    }

    /// <summary>
    /// A worn piece rewritten where it stands with a changed payload is still that piece.
    /// </summary>
    /// <remarks>
    /// A move is not the only thing that reaches a slot: a use case that changed something about a
    /// piece would write it back to the position it is already in. That must not be a new thing
    /// either, so a position rewritten with the same template, kind and value keeps the row it had.
    /// </remarks>
    [Fact]
    public async Task AWornPieceRewrittenWhereItStandsKeepsItsIdentity()
    {
        await using var store = await NewStoreAsync();
        await SettleAsync(store, session => session.Write(Main, Worn(12, Sword(enchant: 2))));

        var identity = await IdentityAtAsync(store.Path, Main, 12);
        var allocated = await HighWaterMarkAsync(store.Path);

        await SettleAsync(store, session => session.Write(Main, Worn(12, Sword(enchant: 9))));

        Assert.Equal(identity, await IdentityAtAsync(store.Path, Main, 12));
        Assert.Equal(allocated, await HighWaterMarkAsync(store.Path));
        Assert.Equal(Sword(enchant: 9), await CoreAtAsync(store.Path, Main, 12));
    }

    /// <summary>
    /// One shape for both kinds of content, differing only in what its three columns say.
    /// </summary>
    /// <remarks>
    /// The worn piece's position names the world row and holds one of it; the pile's names a
    /// template and holds as many as the pile does. Neither position knows anything else about what
    /// it holds, which is what lets a position be rebuilt without losing something.
    /// </remarks>
    [Fact]
    public async Task APositionSaysWhichOfTheTwoItHoldsAndHowMany()
    {
        await using var store = await NewStoreAsync();
        await SettleAsync(store, session =>
        {
            session.Write(Main, Worn(20, Sword(enchant: 1)));
            session.Write(Main, Pile(21, Potion(5)));
        });

        var identity = await IdentityAtAsync(store.Path, Main, 20);

        Assert.Equal(
            new SlotRow("world", identity!.Value, 1), await SlotAtAsync(store.Path, Main, 20));
        Assert.Equal(new SlotRow("template", PotionId, 5), await SlotAtAsync(store.Path, Main, 21));
    }

    /// <summary>
    /// A pile is given no identity, on the way in or on the way anywhere else.
    /// </summary>
    /// <remarks>
    /// Its row is still replaced whenever it moves, exactly as every row used to be, and its
    /// position goes on naming the template rather than the row. Two of it are the same thing, so
    /// there is nothing about the row worth keeping and nothing that could tell that it changed.
    /// </remarks>
    [Fact]
    public async Task APileIsNamedByItsTemplateAndNotByAnyIdentityOfItsOwn()
    {
        await using var store = await NewStoreAsync();
        await SettleAsync(store, session => session.Write(Main, Pile(65, Potion(5))));

        await SettleAsync(store, session =>
        {
            session.Clear(Main, 65);
            session.Write(Main, Pile(66, Potion(5)));
        });

        Assert.Equal(new SlotRow("template", PotionId, 5), await SlotAtAsync(store.Path, Main, 66));
        Assert.Null(await SlotAtAsync(store.Path, Main, 65));
    }

    /// <remarks>
    /// Settlement reads the class off the write and has nothing else to read it off, so a write
    /// that does not say is refused where it is made rather than guessed at where it lands.
    /// </remarks>
    [Fact]
    public async Task AWriteThatDoesNotSayWhatTheArchiveMakesOfItIsRefused()
    {
        await using var store = await NewStoreAsync();
        var session = store.Value.SessionIn(new StubTransaction());

        Assert.Throws<ArgumentException>(
            () => session.Write(Main, new StoredItem(65, Potion(1))));
    }

    private const int PotionId = 1000;

    private const int SwordId = 4000;

    private const int RingId = 4001;

    private const int CreatureItemId = 63000;

    private const int AvatarItemId = 3000;

    private const int AvatarUid = 777;

    private const byte AvatarKind = 8;

    private static StoredItem Worn(int slotIndex, InventoryItemCoreState core) =>
        new(slotIndex, core) { WorldClass = ItemWorldClass.Equipment };

    private static StoredItem Pile(int slotIndex, InventoryItemCoreState core) =>
        new(slotIndex, core) { WorldClass = ItemWorldClass.Interchangeable };

    private static InventoryItemCoreState Potion(int count) => Core(6, PotionId, count);

    /// <summary>A worn piece whose payload differs from another of the same template.</summary>
    private static InventoryItemCoreState Sword(int enchant) =>
        Core(1, SwordId, 0) with { EnchantUpgradeCount = (byte)enchant };

    private static InventoryItemCoreState Ring() => Core(1, RingId, 0);

    private static InventoryItemCoreState Core(byte itemKind, int itemId, int value) =>
        new(itemKind, itemId, value, 0, 0, 0, 0, 0, 0, 0, -1,
            new ChronicleOptionState(0, 0, 0, 0, 0), new ChronicleOptionState(0, 0, 0, 0, 0),
            0, 0, 0, 0, 0,
            new RandomOptionState(0, 0, 0), new RandomOptionState(0, 0, 0),
            new RandomOptionState(0, 0, 0), 0, byte.MaxValue, 0, new RandomOptionState(0, 0, 0),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>What one position of <c>item_slots</c> says it holds.</summary>
    private sealed record SlotRow(string Kind, int Id, int Count);

    private static async Task SettleAsync(
        StoreUnderTest store, Action<IItemDomainStoreSession> work)
    {
        var transaction = new StubTransaction();
        work(store.Value.SessionIn(transaction));
        await transaction.SettleAsync();
    }

    private static async Task<int?> IdentityAtAsync(
        string path, ContainerId containerId, int slotIndex)
    {
        var found = await ScalarAsync(
            path,
            "SELECT core_uid FROM item_slots "
            + "WHERE owner_scope='character' AND owner_id=@cid "
            + "AND list_type=@list AND slot_index=@slot",
            command =>
            {
                command.Parameters.AddWithValue("@cid", Owner.Value);
                command.Parameters.AddWithValue("@list", ListTypeOf(containerId));
                command.Parameters.AddWithValue("@slot", slotIndex);
            });
        return found is null ? null : Convert.ToInt32(found, null);
    }

    private static async Task<InventoryItemCoreState?> CoreAtAsync(
        string path, ContainerId containerId, int slotIndex)
    {
        var found = await ScalarAsync(
            path,
            "SELECT held.item_core FROM item_slots place "
            + "JOIN items held ON held.item_uid=place.core_uid "
            + "WHERE place.owner_scope='character' AND place.owner_id=@cid "
            + "AND place.list_type=@list AND place.slot_index=@slot",
            command =>
            {
                command.Parameters.AddWithValue("@cid", Owner.Value);
                command.Parameters.AddWithValue("@list", ListTypeOf(containerId));
                command.Parameters.AddWithValue("@slot", slotIndex);
            });
        return found is null ? null : InventoryItemCore.Decode((byte[])found);
    }

    private static async Task<SlotRow?> SlotAtAsync(
        string path, ContainerId containerId, int slotIndex)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT kind,id,count FROM item_slots WHERE owner_scope='character' AND owner_id=@cid "
            + "AND list_type=@list AND slot_index=@slot";
        command.Parameters.AddWithValue("@cid", Owner.Value);
        command.Parameters.AddWithValue("@list", ListTypeOf(containerId));
        command.Parameters.AddWithValue("@slot", slotIndex);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new SlotRow(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2))
            : null;
    }

    /// <summary>Every identity this database holds, so a new or missing one is visible.</summary>
    private static async Task<List<long>> IdentitiesAsync(string path)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT item_uid FROM items ORDER BY item_uid";
        var found = new List<long>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            found.Add(reader.GetInt64(0));
        }

        return found;
    }

    /// <summary>The largest identity this database has ever handed out.</summary>
    private static async Task<long> HighWaterMarkAsync(string path) =>
        Convert.ToInt64(
            await ScalarAsync(path, "SELECT seq FROM sqlite_sequence WHERE name='items'", _ => { })
            ?? 0L,
            null);

    private static async Task SetCreatureStateAsync(string path, int identity, int progress)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE items SET creature_field04=0, creature_mode_flag=0, "
            + "creature_progress_value=@progress WHERE item_uid=@uid";
        command.Parameters.AddWithValue("@progress", progress);
        command.Parameters.AddWithValue("@uid", identity);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<int?> CreatureStateAsync(string path, int identity)
    {
        var found = await ScalarAsync(
            path,
            "SELECT creature_progress_value FROM items WHERE item_uid=@uid",
            command => command.Parameters.AddWithValue("@uid", identity));
        return found is null or DBNull ? null : Convert.ToInt32(found, null);
    }

    private static async Task SetAvatarColourAsync(string path, int identity, int colour)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE items SET avatar_color1=@colour WHERE item_uid=@uid";
        command.Parameters.AddWithValue("@colour", colour);
        command.Parameters.AddWithValue("@uid", identity);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<int?> AvatarColourAsync(string path, int identity)
    {
        var found = await ScalarAsync(
            path,
            "SELECT avatar_color1 FROM items WHERE item_uid=@uid",
            command => command.Parameters.AddWithValue("@uid", identity));
        return found is null or DBNull ? null : Convert.ToInt32(found, null);
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

    private static int ListTypeOf(ContainerId containerId)
    {
        Assert.True(containerId.TryGetCharacterOwner(out _, out var containerType));
        return (int)containerType;
    }

    private static async Task<StoreUnderTest> NewStoreAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-worldrow-{Guid.NewGuid():N}.db");
        // The repositories own the schema; this store only writes into it.
        _ = new SqliteDatabase(path, new DomainTransactionScope());
        await using (var connection = Open(path))
        {
            await using var character = connection.CreateCommand();
            character.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'world')";
            await character.ExecuteNonQueryAsync();
        }

        return new StoreUnderTest(path, new SqliteItemDomainStore(path));
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        return connection;
    }

    private sealed record StoreUnderTest(string Path, SqliteItemDomainStore Value) : IAsyncDisposable
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

    /// <summary>Stands in for the coordinator, as the store's own tests do.</summary>
    private sealed class StubTransaction : IDomainTransaction
    {
        private readonly Dictionary<object, ITransactionParticipant> enlisted = [];

        public TParticipant Enlist<TParticipant>(object identity, Func<TParticipant> create)
            where TParticipant : ITransactionParticipant
        {
            if (this.enlisted.TryGetValue(identity, out var existing))
            {
                return (TParticipant)existing;
            }

            var participant = create();
            this.enlisted.Add(identity, participant);
            return participant;
        }

        public async Task SettleAsync()
        {
            foreach (var participant in this.enlisted.Values)
            {
                await participant.PrepareAsync(CancellationToken.None);
            }

            foreach (var participant in this.enlisted.Values)
            {
                await participant.CommitAsync(CancellationToken.None);
            }
        }
    }
}
