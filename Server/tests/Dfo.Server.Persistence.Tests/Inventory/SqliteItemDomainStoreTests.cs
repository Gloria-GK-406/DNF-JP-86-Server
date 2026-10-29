using Dfo.Server.Persistence.Tests.Support;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>Storage, identity, quantity conservation and atomicity through explicit item lifecycle operations.</summary>
public sealed class SqliteItemDomainStoreTests
{
    [Fact]
    public async Task Collection_custody_is_character_only_and_cannot_merge_two_items_into_one_position()
    {
        await using var store = await NewStoreAsync();
        var box = ContainerId.ForCharacter(new(60), InventoryContainerType.CollectionBox);
        await InTransactionAsync(store.Value, async s =>
            await s.PutAsync(box, 256, await s.CreateAsync(Pile(Potion(1)), default), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => InTransactionAsync(store.Value, async s =>
            await s.GetAllAsync(ContainerId.ForAccount(new(6), InventoryContainerType.CollectionBox), default)));
        await Assert.ThrowsAsync<InvalidDataException>(() => InTransactionAsync(store.Value, async s =>
            await s.PutAsync(box, 256, await s.CreateAsync(Pile(Potion(1)), default), default)));
        await InTransactionAsync(store.Value, async s => Assert.Equal(1, (await s.GetAsync(box, 256, default))!.Count));
    }

    [Fact]
    public async Task Entry_inventory_excludes_server_collection_storage_without_losing_the_item()
    {
        await using var store = await NewStoreAsync((65, Potion(2)));
        await using (var connection = Open(store.Path))
            await InsertItemAsync(connection, 256, Stone(1), 241);
        var table = new SqliteCharacterItemTable(new SqliteDatabase(store.Path, new DomainTransactionScope()));
        var ordinary = await table.ReadByCharacterAsync(new(60), default);
        Assert.Single(ordinary);
        Assert.Equal(65, ordinary[0].SlotIndex);
        await using var check = Open(store.Path);
        await using var command = check.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM item_slots WHERE list_type=241";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    private static readonly ContainerId Main =
        ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Main);

    private static readonly ContainerId Equipment =
        ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Equipment);

    [Fact]
    public async Task Mirror_metadata_survives_reload_and_raw_detachment_but_cannot_become_a_bag_item_or_change_its_origin()
    {
        await using var store = await NewStoreAsync();
        var core = StackableItemCore.Create(1, 26596, 123) with { SealFlag=0, EnchantCardId=77 };
        ItemIdentity? uid=null;
        await InTransactionAsync(store.Value, async session => {
            var item=await session.CreateAsync(ItemSnapshot.FromCore(core,false) with {IsMirror=true,MirrorSourceUid=new(987)},default);
            uid=(await session.PutAsync(Equipment,13,item,default)).ItemUid;
        });
        await InTransactionAsync(store.Value, async session => {
            var held=(await session.GetAsync(Equipment,13,default))!;
            Assert.True(held.Snapshot.IsMirror);Assert.Equal(new ItemIdentity(987),held.Snapshot.MirrorSourceUid);
            Assert.Equal(core,held.Snapshot.ToCore());Assert.Equal(uid,held.ItemUid);
            var detached=await session.TakeAsync(held,null,default);
            var returned=await session.PutAsync(Equipment,13,detached,default);
            Assert.True(returned.Snapshot.IsMirror);Assert.Equal(uid,returned.ItemUid);
        });
        await Assert.ThrowsAsync<InvalidDataException>(()=>InTransactionAsync(store.Value,async session=> {
            var held=(await session.GetAsync(Equipment,13,default))!;
            await session.PutAsync(Main,9,await session.TakeAsync(held,null,default),default);
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>InTransactionAsync(store.Value,async session=> {
            var held=(await session.GetAsync(Equipment,13,default))!;
            await session.UpdateAsync(held,x=>x with {IsMirror=false,MirrorSourceUid=null},default);
        }));
        await InTransactionAsync(store.Value,async session=> {
            Assert.True((await session.GetAsync(Equipment,13,default))!.Snapshot.IsMirror);
            Assert.Null(await session.GetAsync(Main,9,default));
        });
    }

    [Fact]
    public async Task Avatar_zero_appearance_survives_storage_but_equipment_zero_and_negative_are_refused()
    {
        await using var store = await NewStoreAsync();
        var avatar = ItemSnapshot.FromCore(StackableItemCore.Create(8, 42609, 123), false,
            avatar: new(0, 0, new byte[30], 0, 0, 0)) with { AppearanceItemId = 0 };
        await InTransactionAsync(store.Value, async session =>
            await session.PutAsync(Equipment, 3, await session.CreateAsync(avatar, default), default));
        await InTransactionAsync(store.Value, async session =>
            Assert.Equal(0, (await session.GetAsync(Equipment, 3, default))!.Snapshot.AppearanceItemId));
        foreach (var invalid in new[] { avatar with { AppearanceItemId = -1 },
            ItemSnapshot.FromCore(StackableItemCore.Create(1, 26596, 123), false) with { AppearanceItemId = 0 } })
            await Assert.ThrowsAsync<InvalidDataException>(() => InTransactionAsync(store.Value, async session =>
                await session.CreateAsync(invalid, default)));
    }

    [Fact]
    public async Task AccountContainersWithTheSameSlotKeepSeparateContents()
    {
        await using var store = await NewStoreAsync();
        var cargo = new ContainerId("account:6:AccountCargo");
        var other = new ContainerId("account:6:Main");
        await InTransactionAsync(store.Value, async session =>
        {
            await session.PutAsync(cargo, 0, await session.CreateAsync(Pile(Potion(5)), default), default);
            await session.PutAsync(other, 0, await session.CreateAsync(Pile(Stone(7)), default), default);
        });
        await InTransactionAsync(store.Value, async session =>
        {
            Assert.Equal(Potion(5), (await session.GetAsync(cargo, 0, default))!.Snapshot.ToCore());
            Assert.Equal(Stone(7), (await session.GetAsync(other, 0, default))!.Snapshot.ToCore());
            Assert.Empty(await session.GetAllAsync(new ContainerId("account:7:Main"), default));
            Assert.Empty(await session.GetAllAsync(Main, default));
        });
    }

    [Fact]
    public async Task AccountCargoQueryExcludesOtherAccountContainers()
    {
        await using var store = await NewStoreAsync();
        await using (var connection = Open(store.Path))
        {
            await InsertAccountItemAsync(connection, 12, 0, Potion(5));
            await InsertAccountItemAsync(connection, 0, 1, Stone(7));
        }
        var table = new SqliteAccountCargoItemTable(new SqliteDatabase(store.Path, new DomainTransactionScope()));
        var row = Assert.Single(await table.ReadByAccountAsync(new AccountId(6), CancellationToken.None));
        Assert.Equal(12, row.ListType);
        Assert.Equal(Potion(5), row.Core);
    }

    [Fact]
    public async Task SchemaUpgradeAllowsDifferentAccountContainersButRejectsDuplicateSlots()
    {
        await using var store = await NewStoreAsync();
        await using (var connection = Open(store.Path))
        {
            await using var legacy = connection.CreateCommand();
            legacy.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_item_slots_account_slot "
                + "ON item_slots(owner_id, slot_index) WHERE owner_scope = 'account'; PRAGMA user_version=0;";
            await legacy.ExecuteNonQueryAsync();
            await InsertAccountItemAsync(connection, 12, 0, Potion(5));
        }

        HandWrittenShape.Adopt(store.Path);
        HandWrittenShape.Adopt(store.Path);
        await using var upgraded = Open(store.Path);
        await InsertAccountItemAsync(upgraded, 0, 0, Stone(7));
        var duplicate = await Assert.ThrowsAsync<SqliteException>(
            () => InsertAccountItemAsync(upgraded, 12, 0, Potion(9)));
        Assert.Equal(19, duplicate.SqliteErrorCode);
        await using var query = upgraded.CreateCommand();
        query.CommandText = "SELECT count(*) FROM item_slots WHERE owner_scope='account' AND owner_id=6 AND slot_index=0";
        Assert.Equal(2L, await query.ExecuteScalarAsync());
    }

    private static async Task InsertAccountItemAsync(SqliteConnection connection, int listType, int slot,
        InventoryItemCoreState core)
    {
        await InsertItemAsync(connection, slot, core, listType);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE item_slots SET owner_scope='account',owner_id=6 "
            + "WHERE owner_scope='character' AND owner_id=60 AND list_type=@list AND slot_index=@slot";
        command.Parameters.AddWithValue("@list", listType);
        command.Parameters.AddWithValue("@slot", slot);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task AccountCargo_TransfersAnInstanceAcrossCharactersWithoutChangingItsIdentity()
    {
        await using var store = await NewStoreAsync((9, Core(1, 1234, 5678)));
        var cargo = new ContainerId("account:6:AccountCargo");
        var otherAccount = new ContainerId("account:7:AccountCargo");
        var otherCharacter = ContainerId.ForCharacter(new CharacterId(61), InventoryContainerType.Main);
        ItemSnapshot? original = null;
        await InTransactionAsync(store.Value, async session =>
        {
            var source = (await session.GetAsync(Main, 9, default))!;
            original = source.Snapshot;
            await session.PutAsync(cargo, 0, await session.TakeAsync(source, null, default), default);
        });
        await InTransactionAsync(store.Value, async session =>
        {
            Assert.Null(await session.GetAsync(Main, 9, default));
            Assert.Empty(await session.GetAllAsync(otherAccount, default));
            var stored = (await session.GetAsync(cargo, 0, default))!;
            Assert.Equal(original, stored.Snapshot);
            await session.PutAsync(otherCharacter, 9, await session.TakeAsync(stored, null, default), default);
        });
        await InTransactionAsync(store.Value, async session =>
        {
            Assert.Empty(await session.GetAllAsync(cargo, default));
            Assert.Equal(original, (await session.GetAsync(otherCharacter, 9, default))!.Snapshot);
        });
    }

    [Fact]
    public async Task Relocate_TakesAndPutsTheSameRecord()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        await InTransactionAsync(store.Value, async session =>
        {
            var source = (await session.GetAsync(Main, 65, default))!;
            await session.PutAsync(Main, 3, await session.TakeAsync(source, null, default), default);
        });
        Assert.Equal([(3, PotionId, 5)], await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task Split_TakesOnlyTheRequestedQuantity()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        await InTransactionAsync(store.Value, async session =>
        {
            var source = (await session.GetAsync(Main, 65, default))!;
            await session.PutAsync(Main, 66, await session.TakeAsync(source, 2, default), default);
        });
        Assert.Equal([(65, PotionId, 3), (66, PotionId, 2)], await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task Swap_TakesBothRecordsBeforePuttingEitherWithNoParkedSlot()
    {
        await using var store = await NewStoreAsync((65, Potion(5)), (66, Stone(1)));
        await InTransactionAsync(store.Value, async session =>
        {
            var first = await session.TakeAsync((await session.GetAsync(Main, 65, default))!, null, default);
            var second = await session.TakeAsync((await session.GetAsync(Main, 66, default))!, null, default);
            await session.PutAsync(Main, 66, first, default);
            await session.PutAsync(Main, 65, second, default);
        });
        Assert.Equal([(65, StoneId, 1), (66, PotionId, 5)], await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task Merge_AcceptsTheTakenRecordIntoTheExistingPile()
    {
        await using var store = await NewStoreAsync((65, Potion(5)), (66, Potion(2)));
        await InTransactionAsync(store.Value, async session =>
        {
            var source = (await session.GetAsync(Main, 65, default))!;
            await session.PutAsync(Main, 66, await session.TakeAsync(source, null, default), default);
        });
        Assert.Equal([(66, PotionId, 7)], await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task PartialConsume_TakesAndDeletesOnlyTheConsumedQuantity()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        await InTransactionAsync(store.Value, async session =>
        {
            var source = (await session.GetAsync(Main, 65, default))!;
            await session.DeleteItemAsync(await session.TakeAsync(source, 1, default), default);
        });
        Assert.Equal([(65, PotionId, 4)], await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task ConsumingTheLastOfAPile_DeletesItsTakenRecord()
    {
        await using var store = await NewStoreAsync((65, Potion(1)));
        await InTransactionAsync(store.Value, async session =>
        {
            var source = (await session.GetAsync(Main, 65, default))!;
            await session.DeleteItemAsync(await session.TakeAsync(source, null, default), default);
        });
        Assert.Empty(await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task TheWorldRefusesToPlaceAnAvatarRecordTwice()
    {
        var detail = new InventoryAvatarDetailState(11, 12, Enumerable.Repeat((byte)13, 30).ToArray(), 14, 15, 16);
        await using var store = await NewStoreAsync((9, Avatar()));
        await SeedAvatarDetailAsync(store.Path, AvatarUid, detail);
        await InTransactionAsync(store.Value, async session =>
        {
            var item = await session.TakeAsync((await session.GetAsync(Main, 9, default))!, null, default);
            await session.PutAsync(Main, 10, item, default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.PutAsync(Main, 11, item, default).AsTask());
        });
        Assert.Equal([(10, AvatarItemId, AvatarUid)], await ReadSlotsAsync(store.Path));
        Assert.Equivalent(detail, await ReadAvatarDetailAsync(store.Path, AvatarUid));
    }

    [Fact]
    public async Task Relocate_LeavesAnAvatarsRowStandingWithItsFactsUntouched()
    {
        var detail = new InventoryAvatarDetailState(11, 12, Enumerable.Repeat((byte)13, 30).ToArray(), 14, 15, 16);
        await using var store = await NewStoreAsync((9, Avatar()));
        await SeedAvatarDetailAsync(store.Path, AvatarUid, detail);
        var identity = await ReadWorldRowIdAsync(store.Path, 9);
        await InTransactionAsync(store.Value, async session =>
        {
            var item = await session.TakeAsync((await session.GetAsync(Main, 9, default))!, null, default);
            await session.PutAsync(Main, 10, item, default);
        });
        Assert.Equal(identity, await ReadWorldRowIdAsync(store.Path, 10));
        Assert.Equal([(10, AvatarItemId, AvatarUid)], await ReadSlotsAsync(store.Path));
        Assert.Equivalent(detail, await ReadAvatarDetailAsync(store.Path, AvatarUid));
    }

    [Fact]
    public async Task UpdatingThenMovingAnAvatarKeepsItsIdentityAndAllSixFacts()
    {
        var detail = new InventoryAvatarDetailState(41, 42, Enumerable.Repeat((byte)43, 30).ToArray(), 44, 45, 46);
        await using var store = await NewStoreAsync((9, Avatar()));
        await SeedAvatarDetailAsync(store.Path, AvatarUid, detail);
        var standing = await ReadWorldRowIdAsync(store.Path, 9);
        await InTransactionAsync(store.Value, async session =>
        {
            var source = (await session.GetAsync(Main, 9, default))!;
            var updated = await session.UpdateAsync(source, item => item with { Durability = 77 }, default);
            var moved = await session.PutAsync(Main, 10, await session.TakeAsync(updated, null, default), default);
            Assert.Equal((ushort)77, moved.Snapshot.Durability);
        });
        Assert.NotNull(standing);
        Assert.Equal(standing, await ReadWorldRowIdAsync(store.Path, 10));
        var stored = await ReadAvatarDetailAsync(store.Path, AvatarUid);
        Assert.NotNull(stored);
        Assert.Equal(41, stored.ExpireDate);
        Assert.Equal(42, stored.ClearAvatarId);
        Assert.Equal(Enumerable.Repeat((byte)43, 30), stored.JewelSocket);
        Assert.Equal(44, (int)stored.Color1);
        Assert.Equal(45, (int)stored.Color2);
        Assert.Equal(46, stored.DeleteDate);
    }

    [Fact]
    public async Task CreationCarriesAllSixAvatarFactsOntoItsNewRow()
    {
        var detail = new InventoryAvatarDetailState(21, 22, Enumerable.Repeat((byte)23, 30).ToArray(), 24, 25, 26);
        await using var store = await NewStoreAsync();
        await InTransactionAsync(store.Value, async session =>
        {
            var created = await session.CreateAsync(ItemSnapshot.FromCore(Avatar(), false, avatar: detail), default);
            Assert.NotNull(created.ItemUid);
            await session.PutAsync(Main, 9, created, default);
        });
        Assert.Equal([(9, AvatarItemId, AvatarUid)], await ReadSlotsAsync(store.Path));
        var stored = await ReadAvatarDetailAsync(store.Path, AvatarUid);
        Assert.NotNull(stored);
        Assert.Equal(21, stored.ExpireDate);
        Assert.Equal(22, stored.ClearAvatarId);
        Assert.Equal(Enumerable.Repeat((byte)23, 30), stored.JewelSocket);
        Assert.Equal(24, (int)stored.Color1);
        Assert.Equal(25, (int)stored.Color2);
        Assert.Equal(26, stored.DeleteDate);
    }

    [Fact]
    public async Task Read_SeesThisSessionsOwnUncommittedWrites()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var store = await NewStoreAsync((65, Potion(5)));
            var scope = new DomainTransactionScope();
            await using var transaction = scope.Begin();
            var session = store.Value.SessionIn(scope.Current!);
            await session.PutAsync(Main, 66, await session.CreateAsync(Pile(Potion(2)), default), default);
            await session.DeleteItemAsync(await session.TakeAsync((await session.GetAsync(Main, 65, default))!, null, default), default);
            Assert.Equal(2, (await session.GetAsync(Main, 66, default))!.Count);
            Assert.Null(await session.GetAsync(Main, 65, default));
            Assert.Equal([66], (await session.GetAllAsync(Main, default)).Select(item => item.SlotIndex));
            // SQL writes are immediate within the transaction, but remain invisible to another connection.
            Assert.Equal([(65, PotionId, 5)], await ReadSlotsAsync(store.Path));
        });
    }

    [Fact]
    public async Task Rollback_LeavesTheStoredItemsUntouched()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        await using (var transaction = new ObservedTransaction())
        {
            var session = store.Value.SessionIn(transaction);
            var moving = await session.TakeAsync((await session.GetAsync(Main, 65, default))!, null, default);
            await session.PutAsync(Main, 3, moving, default);
            // Exercise rollback after the durable participant has prepared its immediate writes.
            await Assert.Single(transaction.Participants, participant => participant.CommitCanFail).PrepareAsync(default);
        }
        Assert.Equal([(65, PotionId, 5)], await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task Session_TakesPartOnceHoweverOftenItsStoreIsAsked()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        await using var transaction = new ObservedTransaction();
        var first = store.Value.SessionIn(transaction);
        var second = store.Value.SessionIn(transaction);
        Assert.Same(first, second);
        Assert.Single(transaction.Participants, participant => participant.CommitCanFail);
        Assert.Single(transaction.Participants, participant => !participant.CommitCanFail);
    }

    [Fact]
    public async Task Store_DefinesNoTableOfItsOwn()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        var before = await ReadTableNamesAsync(store.Path);
        await InTransactionAsync(store.Value, async session =>
        {
            _ = await session.GetAllAsync(Main, default);
            await session.PutAsync(Main, 66, await session.CreateAsync(Pile(Potion(1)), default), default);
        });
        Assert.Equal(before, await ReadTableNamesAsync(store.Path));
    }

    [Fact]
    public async Task Store_IsTheOnlyDurableParticipantWhoseCommitCanFail()
    {
        await using var store = await NewStoreAsync();
        await using var transaction = new ObservedTransaction();
        _ = store.Value.SessionIn(transaction);
        Assert.True(store.Value.IsDurable);
        Assert.IsType<SqliteDatabaseParticipant>(Assert.Single(transaction.Participants, participant => participant.CommitCanFail));
        Assert.IsType<ItemLifecycleScope>(Assert.Single(transaction.Participants, participant => !participant.CommitCanFail));
    }

    [Fact]
    public async Task TwoContainersExchangeTheirContentsInOneSettlement()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        await SeedAsync(store.Path, Equipment, 1, Stone(1));
        await InTransactionAsync(store.Value, async session =>
        {
            var fromMain = await session.TakeAsync((await session.GetAsync(Main, 65, default))!, null, default);
            var fromEquipment = await session.TakeAsync((await session.GetAsync(Equipment, 1, default))!, null, default);
            await session.PutAsync(Main, 65, fromEquipment, default);
            await session.PutAsync(Equipment, 1, fromMain, default);
        });
        Assert.Equal([(65, StoneId, 1)], await ReadSlotsAsync(store.Path));
        Assert.Equal([(1, PotionId, 5)], await ReadSlotsAsync(store.Path, Equipment));
    }

    [Fact]
    public async Task TakingAndDeletingANewlyPlacedItemLeavesItsSlotEmpty()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        await InTransactionAsync(store.Value, async session =>
        {
            var added = await session.PutAsync(Main, 66, await session.CreateAsync(Pile(Potion(2)), default), default);
            await session.DeleteItemAsync(await session.TakeAsync(added, null, default), default);
            Assert.Null(await session.GetAsync(Main, 66, default));
        });
        Assert.Equal([(65, PotionId, 5)], await ReadSlotsAsync(store.Path));
    }

    [Theory]
    [InlineData("character:60:AccountCargo")]
    [InlineData("run:1:Main")]
    [InlineData("account:6:QuickSlot")]
    [InlineData("account:6:99")]
    public async Task AContainerThisStoreKeepsNoRowsForIsRefused(string containerName)
    {
        await using var store = await NewStoreAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => InTransactionAsync(store.Value, async session =>
        {
            await session.GetAllAsync(new ContainerId(containerName), default);
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => InTransactionAsync(store.Value, async session =>
        {
            var created = await session.CreateAsync(Pile(Potion(1)), default);
            await session.PutAsync(new ContainerId(containerName), 0, created, default);
        }));
        Assert.Empty(await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task ANegativeSlotIsRefused()
    {
        await using var store = await NewStoreAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => InTransactionAsync(store.Value, async session =>
        {
            await session.GetAsync(Main, -1, default);
        }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => InTransactionAsync(store.Value, async session =>
        {
            var created = await session.CreateAsync(Pile(Potion(1)), default);
            await session.PutAsync(Main, -1, created, default);
        }));
        Assert.Empty(await ReadSlotsAsync(store.Path));
    }

    [Fact]
    public async Task Commit_RefusesToStandInForAPrepareThatNeverRan()
    {
        await using var store = await NewStoreAsync((65, Potion(5)));
        await using var transaction = new ObservedTransaction();
        var session = store.Value.SessionIn(transaction);
        await session.PutAsync(Main, 3, await session.CreateAsync(Pile(Potion(1)), default), default);
        var participant = Assert.Single(transaction.Participants, participant => participant.CommitCanFail);
        await Assert.ThrowsAsync<InvalidOperationException>(() => participant.CommitAsync(default).AsTask());
        Assert.Equal([(65, PotionId, 5)], await ReadSlotsAsync(store.Path));
    }

    private static ItemSnapshot Pile(InventoryItemCoreState core) => ItemSnapshot.FromCore(core, true);

    private static async Task InTransactionAsync(SqliteItemDomainStore store, Func<IItemDomainStoreSession, ValueTask> action)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = new DomainTransactionScope();
            await using var transaction = scope.Begin();
            await action(store.SessionIn(scope.Current!));
            await transaction.CommitAsync(default);
        });
    }

    private const int PotionId = 1000;

    private const int StoneId = 2000;

    private static InventoryItemCoreState Potion(int count) => Core(6, PotionId, count);

    private static InventoryItemCoreState Stone(int count) => Core(6, StoneId, count);

    private const int AvatarItemId = 3000;

    /// <summary>An avatar row carries its detail row's key in the core's value field.</summary>
    private const int AvatarUid = 777;

    /// <summary>An avatar UID as the four bytes a core carries it as.</summary>
    /// <remarks>
    /// The row keeps no column of its own for the UID any more, so a test that wants the row of
    /// one avatar asks the core, which is where the value has always been.
    /// </remarks>
    private static byte[] AvatarKeyOf(int avatarUid)
    {
        var key = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(key, avatarUid);
        return key;
    }

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

        return new StoreUnderTest(path, new SqliteItemDomainStore(path));
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
            "INSERT INTO items(item_core) VALUES(@core);"
            + "INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) "
            + "VALUES('character',60,@list,@slot,@kind,CASE WHEN @kind='template' THEN @id ELSE last_insert_rowid() END,"
            + "last_insert_rowid(),@count)";
        command.Parameters.AddWithValue("@kind", core.ItemKind == 6 ? "template" : "world");
        command.Parameters.AddWithValue("@id", core.ItemId);
        command.Parameters.AddWithValue("@count", core.ItemKind == 6 ? core.Value : 1);
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
            "UPDATE items SET avatar_expire_date=@expire,avatar_clear_id=@clear,"
            + "avatar_jewel_socket=@jewel,avatar_color1=@color1,avatar_color2=@color2,"
            + "avatar_delete_date=@delete "
            + "WHERE substr(item_core,1,1)=X'08' AND substr(item_core,6,4)=@uid";
        command.Parameters.AddWithValue("@uid", AvatarKeyOf(avatarUid));
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
            + "FROM items WHERE avatar_expire_date IS NOT NULL "
            + "AND substr(item_core,6,4)=@uid";
        command.Parameters.AddWithValue("@uid", AvatarKeyOf(avatarUid));
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return new InventoryAvatarDetailState(
            reader.GetInt32(0), reader.GetInt32(1), (byte[])reader.GetValue(2),
            (ushort)reader.GetInt32(3), (ushort)reader.GetInt32(4), reader.GetInt32(5));
    }

    /// <summary>Which row of the world a position names, or nothing when the position is empty.</summary>
    /// <remarks>
    /// <see cref="ReadSlotsAsync"/> answers what a position holds, which is the same for a row that
    /// survived and a row that replaced it. This answers which row, which is what tells the two
    /// apart.
    /// </remarks>
    private static async Task<long?> ReadWorldRowIdAsync(string path, int slot)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT core_uid FROM item_slots "
            + "WHERE owner_scope='character' AND owner_id=60 AND list_type=0 AND slot_index=@slot";
        command.Parameters.AddWithValue("@slot", slot);
        return await command.ExecuteScalarAsync() as long?;
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
            "SELECT place.slot_index,held.item_core "
            + "FROM item_slots place JOIN items held ON held.item_uid=place.core_uid "
            + "WHERE place.owner_scope='character' AND place.owner_id=60 "
            + "AND place.list_type=@list ORDER BY place.slot_index";
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

    /// <summary>Observes registrations while the real coordinator retains ordering and rollback responsibility.</summary>
    private sealed class ObservedTransaction : IDomainTransaction, IAsyncDisposable
    {
        private readonly DomainTransaction transaction = new();
        public List<ITransactionParticipant> Participants { get; } = [];
        public TParticipant Enlist<TParticipant>(object identity, Func<TParticipant> create)
            where TParticipant : ITransactionParticipant => transaction.Enlist(identity, () =>
            {
                var participant = create();
                Participants.Add(participant);
                return participant;
            });
        public ValueTask DisposeAsync() => transaction.RollbackAsync();
    }
}
