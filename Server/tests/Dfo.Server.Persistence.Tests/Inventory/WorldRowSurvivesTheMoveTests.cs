using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

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

    [Fact]
    public async Task AWornPieceKeepsOneIdentityThroughAMoveAWearAndATakeOff()
    {
        await using var store = await NewStoreAsync();
        var sword = Sword(enchant: 7);
        await SettleAsync(store, async session =>
        {
            await session.PutAsync(Main, 65, await session.CreateAsync(Worn(sword), default), default);
        });
        var identity = await IdentityAtAsync(store.Path, Main, 65);
        var allocated = await HighWaterMarkAsync(store.Path);
        foreach (var (from, fromSlot, to, toSlot) in new[]
                 { (Main, 65, Main, 3), (Main, 3, Equipment, 5), (Equipment, 5, Main, 70) })
        {
            await SettleAsync(store, async session =>
            {
                var moving = await session.TakeAsync((await session.GetAsync(from, fromSlot, default))!, null, default);
                await session.PutAsync(to, toSlot, moving, default);
            });
            Assert.Null(await IdentityAtAsync(store.Path, from, fromSlot));
            Assert.Equal(identity, await IdentityAtAsync(store.Path, to, toSlot));
            Assert.Equal(allocated, await HighWaterMarkAsync(store.Path));
            Assert.Equal(new SlotRow("world", identity!.Value, 1), await SlotAtAsync(store.Path, to, toSlot));
            Assert.Null(await SlotAtAsync(store.Path, from, fromSlot));
        }
        Assert.Equal(sword, await CoreAtAsync(store.Path, Main, 70));
    }

    [Fact]
    public async Task AMoveNeitherDeletesNorBuildsAWorldRow()
    {
        await using var store = await NewStoreAsync();
        await SettleAsync(store, async session =>
        {
            await session.PutAsync(Main, 65, await session.CreateAsync(Worn(Sword(enchant: 3)), default), default);
            await session.PutAsync(Main, 66, await session.CreateAsync(Worn(Ring()), default), default);
        });
        var before = await IdentitiesAsync(store.Path);
        var allocated = await HighWaterMarkAsync(store.Path);
        await SettleAsync(store, async session =>
        {
            var moving = await session.TakeAsync((await session.GetAsync(Main, 65, default))!, null, default);
            await session.PutAsync(Equipment, 2, moving, default);
        });
        Assert.Equal(before, await IdentitiesAsync(store.Path));
        Assert.Equal(allocated, await HighWaterMarkAsync(store.Path));
    }

    [Fact]
    public async Task TwoWornPiecesSwappingPositionsEachKeepTheirOwnIdentity()
    {
        await using var store = await NewStoreAsync();
        var sword = Sword(enchant: 4);
        var ring = Ring();
        await SettleAsync(store, async session =>
        {
            await session.PutAsync(Main, 10, await session.CreateAsync(Worn(sword), default), default);
            await session.PutAsync(Main, 11, await session.CreateAsync(Worn(ring), default), default);
        });
        var swordIdentity = await IdentityAtAsync(store.Path, Main, 10);
        var ringIdentity = await IdentityAtAsync(store.Path, Main, 11);
        var allocated = await HighWaterMarkAsync(store.Path);
        await SettleAsync(store, async session =>
        {
            var first = await session.TakeAsync((await session.GetAsync(Main, 10, default))!, null, default);
            var second = await session.TakeAsync((await session.GetAsync(Main, 11, default))!, null, default);
            await session.PutAsync(Main, 11, first, default);
            await session.PutAsync(Main, 10, second, default);
        });
        Assert.Equal(swordIdentity, await IdentityAtAsync(store.Path, Main, 11));
        Assert.Equal(ringIdentity, await IdentityAtAsync(store.Path, Main, 10));
        Assert.Equal(allocated, await HighWaterMarkAsync(store.Path));
        Assert.Equal(sword, await CoreAtAsync(store.Path, Main, 11));
        Assert.Equal(ring, await CoreAtAsync(store.Path, Main, 10));
    }

    [Fact]
    public async Task AContractCreatureTakenOffIntoTheBagIsStillTheSameCreature()
    {
        await using var store = await NewStoreAsync();
        var creature = ItemSnapshot.FromCore(Core(5, CreatureItemId, 0), false,
            creature: new InventoryCreatureDetailState(0, 0, 0, 0, 0, 0, [], 0));
        await SettleAsync(store, async session =>
        {
            await session.PutAsync(Equipment, 24, await session.CreateAsync(creature, default), default);
        });
        var identity = await IdentityAtAsync(store.Path, Equipment, 24);
        Assert.NotNull(identity);
        await SetCreatureStateAsync(store.Path, identity.Value, progress: 4242);
        await SettleAsync(store, async session =>
        {
            var moving = await session.TakeAsync((await session.GetAsync(Equipment, 24, default))!, null, default);
            await session.PutAsync(Main, 80, moving, default);
        });
        Assert.Equal(identity, await IdentityAtAsync(store.Path, Main, 80));
        Assert.Equal(4242, await CreatureStateAsync(store.Path, identity.Value));
        Assert.Equal(identity.Value, (await CoreAtAsync(store.Path, Main, 80))!.Value);
    }

    [Fact]
    public async Task AnAvatarsOwnColumnsStayWhereTheyAreThroughAMove()
    {
        await using var store = await NewStoreAsync();
        var avatar = ItemSnapshot.FromCore(Core(AvatarKind, AvatarItemId, AvatarUid), false,
            avatar: new InventoryAvatarDetailState(0, 0, new byte[30], 0, 0, 0));
        await SettleAsync(store, async session =>
        {
            await session.PutAsync(Main, 9, await session.CreateAsync(avatar, default), default);
        });
        var identity = await IdentityAtAsync(store.Path, Main, 9);
        Assert.NotNull(identity);
        await SetAvatarColourAsync(store.Path, identity.Value, colour: 321);
        await SettleAsync(store, async session =>
        {
            var moving = await session.TakeAsync((await session.GetAsync(Main, 9, default))!, null, default);
            await session.PutAsync(Main, 10, moving, default);
        });
        Assert.Equal(identity, await IdentityAtAsync(store.Path, Main, 10));
        Assert.Equal(321, await AvatarColourAsync(store.Path, identity.Value));
    }

    [Fact]
    public async Task AWornPieceUpdatedWhereItStandsKeepsItsIdentity()
    {
        await using var store = await NewStoreAsync();
        await SettleAsync(store, async session =>
        {
            await session.PutAsync(Main, 12, await session.CreateAsync(Worn(Sword(enchant: 2)), default), default);
        });
        var identity = await IdentityAtAsync(store.Path, Main, 12);
        var allocated = await HighWaterMarkAsync(store.Path);
        await SettleAsync(store, async session =>
        {
            await session.UpdateAsync((await session.GetAsync(Main, 12, default))!,
                snapshot => snapshot with { EnchantUpgradeCount = 9 }, default);
        });
        Assert.Equal(identity, await IdentityAtAsync(store.Path, Main, 12));
        Assert.Equal(allocated, await HighWaterMarkAsync(store.Path));
        Assert.Equal(Sword(enchant: 9), await CoreAtAsync(store.Path, Main, 12));
    }

    [Fact]
    public async Task APositionSaysWhichOfTheTwoItHoldsAndHowMany()
    {
        await using var store = await NewStoreAsync();
        await SettleAsync(store, async session =>
        {
            await session.PutAsync(Main, 20, await session.CreateAsync(Worn(Sword(enchant: 1)), default), default);
            await session.PutAsync(Main, 21, await session.CreateAsync(Pile(Potion(5)), default), default);
        });
        var identity = await IdentityAtAsync(store.Path, Main, 20);
        Assert.Equal(new SlotRow("world", identity!.Value, 1), await SlotAtAsync(store.Path, Main, 20));
        Assert.Equal(new SlotRow("template", PotionId, 5), await SlotAtAsync(store.Path, Main, 21));
    }

    [Fact]
    public async Task APileIsNamedByItsTemplateAndNotByAnyIdentityOfItsOwn()
    {
        await using var store = await NewStoreAsync();
        await SettleAsync(store, async session =>
        {
            var item = await session.PutAsync(Main, 65, await session.CreateAsync(Pile(Potion(5)), default), default);
            Assert.Null(item.ItemUid);
        });
        await SettleAsync(store, async session =>
        {
            var moving = await session.TakeAsync((await session.GetAsync(Main, 65, default))!, null, default);
            Assert.Null(moving.ItemUid);
            var item = await session.PutAsync(Main, 66, moving, default);
            Assert.Null(item.ItemUid);
        });
        Assert.Equal(new SlotRow("template", PotionId, 5), await SlotAtAsync(store.Path, Main, 66));
        Assert.Null(await SlotAtAsync(store.Path, Main, 65));
    }

    [Fact]
    public async Task ACreationWhoseTraitsDisagreeWithItsLegacyKindIsRefused()
    {
        await using var store = await NewStoreAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => SettleAsync(store, async session =>
        {
            _ = await session.CreateAsync(Pile(Potion(1)) with { Type = ItemTraits.Avatar }, default);
        }));
        Assert.Empty(await IdentitiesAsync(store.Path));
    }

    private const int PotionId = 1000;

    private const int SwordId = 4000;

    private const int RingId = 4001;

    private const int CreatureItemId = 63000;

    private const int AvatarItemId = 3000;

    private const int AvatarUid = 777;

    private const byte AvatarKind = 8;

    private static ItemSnapshot Worn(InventoryItemCoreState core) => ItemSnapshot.FromCore(core, false);

    private static ItemSnapshot Pile(InventoryItemCoreState core) => ItemSnapshot.FromCore(core, true);

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
        StoreUnderTest store, Func<IItemDomainStoreSession, ValueTask> work)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = TestRequired.TransactionScope();
            await using var transaction = scope.Begin();
            await work(store.Value.SessionIn(scope.Current!));
            await transaction.CommitAsync(CancellationToken.None);
        });
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
        _ = new SqliteDatabase(path, TestRequired.TransactionScope());
        await using (var connection = Open(path))
        {
            await using var character = connection.CreateCommand();
            character.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'world')";
            await character.ExecuteNonQueryAsync();
        }

        return new StoreUnderTest(path, new SqliteItemDomainStore(path, TestRequired.NoContentsEvents));
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

}
