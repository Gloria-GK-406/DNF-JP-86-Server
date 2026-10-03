using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;
using Xunit;
using Dfo.Server.Persistence.Tests.Support;


namespace Dfo.Server.Persistence.Tests.Inventory.Lifecycle;

public sealed class StorageLifecycleTests
{
    private static readonly ContainerId Main = new("character:60:Main");
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static ItemSnapshot Sword() => new() { ItemId = 123, LegacyKind = 1, Type = ItemTraits.Equipment, Count = 1, Durability = 31, TailUnknown3 = 93 };
    [Fact]
    public async Task UnplacedRecordAfterFlushRejectsCommitAndRestoresSource()
    {
        using var db = new Database();
        var seed = new DomainTransaction(); var s = db.Store.SessionIn(seed);
        await s.PutAsync(Main, 10, await s.CreateAsync(Sword(), Ct), Ct);
        await seed.CommitAsync(Ct);
        var tx = new DomainTransaction(); s = db.Store.SessionIn(tx);
        var old = (await s.GetAsync(Main, 10, Ct))!;
        _ = await s.TakeAsync(old, null, Ct);
        Assert.Null(await s.GetAsync(Main, 10, Ct));
        Assert.Equal(1L, await db.Rows(tx));
        await s.ApplyDecidedWritesAsync(Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync(Ct).AsTask());
        var check = new DomainTransaction();
        Assert.Equal(old.ItemUid, (await db.Store.SessionIn(check).GetAsync(Main, 10, Ct))!.ItemUid);
        await check.RollbackAsync();
    }
    private static ItemSnapshot Pile(int count) => new() { ItemId = 8474, LegacyKind = 2, Type = ItemTraits.Consumable, IsPile = true, Count = count, TailUnknown2 = 72 };
    private static async Task Seed(Database db, ItemSnapshot snapshot, int slot = 10)
    {
        var tx = new DomainTransaction(); var session = db.Store.SessionIn(tx);
        await session.PutAsync(Main, slot, await session.CreateAsync(snapshot, Ct), Ct); await tx.CommitAsync(Ct);
    }
    [Fact]
    public async Task SameUidReturningToSameSlotNeverRevivesOldReference()
    {
        using var db = new Database(); await Seed(db, Sword());
        var tx = new DomainTransaction(); var s = db.Store.SessionIn(tx);
        var old = (await s.GetAsync(Main, 10, Ct))!;
        var record = await s.TakeAsync(old, null, Ct);
        Assert.Equal(1L, await db.Rows(tx));
        var next = await s.PutAsync(Main, 10, record, Ct);
        Assert.Equal(old.ItemUid, next.ItemUid);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.UpdateAsync(old, x => x with { Durability = 0 }, Ct).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.PutAsync(Main, 11, record, Ct).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.DeleteItemAsync(record, Ct).AsTask());
        await tx.CommitAsync(Ct);
    }
    [Fact]
    public async Task ForgedRecordAndCrossTransactionReferenceHaveNoWriteAuthority()
    {
        using var db = new Database(); await Seed(db, Sword());
        var read = new DomainTransaction(); var old = (await db.Store.SessionIn(read).GetAsync(Main, 10, Ct))!;
        await read.RollbackAsync(); Assert.Equal(123, old.Snapshot.ItemId);
        var tx = new DomainTransaction(); var s = db.Store.SessionIn(tx);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.PutAsync(Main, 11, new ItemRecord(old.Snapshot), Ct).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.UpdateAsync(old, x => x with { Durability = 0 }, Ct).AsTask());
        await tx.RollbackAsync();
    }
    [Fact]
    public async Task RejectedPutLeavesRecordAvailableAndExchangeKeepsBothUids()
    {
        using var db = new Database(); await Seed(db, Sword()); await Seed(db, Sword() with { ItemId = 124 }, 11);
        var tx = new DomainTransaction(); var s = db.Store.SessionIn(tx);
        var left = (await s.GetAsync(Main, 10, Ct))!; var right = (await s.GetAsync(Main, 11, Ct))!;
        var a = await s.TakeAsync(left, null, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.PutAsync(Main, 11, a, Ct).AsTask());
        var b = await s.TakeAsync(right, null, Ct);
        await s.PutAsync(Main, 11, a, Ct); await s.PutAsync(Main, 10, b, Ct);
        Assert.Equal(right.ItemUid, (await s.GetAsync(Main, 10, Ct))!.ItemUid);
        Assert.Equal(left.ItemUid, (await s.GetAsync(Main, 11, Ct))!.ItemUid);
        await tx.RollbackAsync();
        var check = new DomainTransaction();
        Assert.Equal(left.ItemUid, (await db.Store.SessionIn(check).GetAsync(Main, 10, Ct))!.ItemUid);
        await check.RollbackAsync();
    }
    [Fact]
    public async Task PartialTakeSplitMergeAndDeleteConserveMaterialQuantity()
    {
        using var db = new Database(); await Seed(db, Pile(20));
        var tx = new DomainTransaction(); var s = db.Store.SessionIn(tx); var reference = (await s.GetAsync(Main, 10, Ct))!;
        var piece = await s.TakeAsync(reference, 8, Ct);
        Assert.Equal(12, (await s.GetAsync(Main, 10, Ct))!.Count);
        var (taken, remainder) = await ItemLifecycleScope.In(tx).SplitAsync(piece, 3, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.PutAsync(Main, 11, piece, Ct).AsTask());
        await s.PutAsync(Main, 10, taken, Ct);
        await s.PutAsync(Main, 11, remainder, Ct);
        Assert.Equal(15, (await s.GetAsync(Main, 10, Ct))!.Count);
        Assert.Equal(5, (await s.GetAsync(Main, 11, Ct))!.Count);
        var deleted = await s.TakeAsync(reference, 2, Ct);
        await ItemLifecycleScope.In(tx).DeleteItemAsync(deleted, Ct);
        Assert.Equal(13, (await s.GetAsync(Main, 10, Ct))!.Count);
        Assert.Equal(2L, await db.Rows(tx));
        await tx.CommitAsync(Ct);
    }
    [Fact]
    public async Task ChangedPileAttributesRefuseMergeWithoutConsumingRecord()
    {
        using var db = new Database(); await Seed(db, Pile(2));
        var tx = new DomainTransaction(); var s = db.Store.SessionIn(tx);
        var other = await s.CreateAsync(Pile(4) with { TailUnknown2 = 73 }, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.PutAsync(Main, 10, other, Ct).AsTask());
        await s.PutAsync(Main, 11, other, Ct); await tx.CommitAsync(Ct);
    }
    [Fact]
    public async Task UpdateUsesLatestStateAndBusinessViewRetainsStorageAddress()
    {
        using var db = new Database(); await Seed(db, Sword(), 57);
        var tx = new DomainTransaction(); var s = db.Store.SessionIn(tx);
        var first = (await s.GetAsync(Main, 57, Ct))!.WithBusinessSlot(new(BusinessInventorySpace.CarriedEquipment, 0));
        var second = (await s.GetAsync(Main, 57, Ct))!;
        await s.UpdateAsync(first, x => x with { Durability = 11 }, Ct);
        var updated = await s.UpdateAsync(second, x => x with { TailUnknown1 = 55 }, Ct);
        Assert.Equal(11, updated.Snapshot.Durability); Assert.Equal(55, updated.Snapshot.TailUnknown1);
        Assert.Equal(0, first.SlotIndex); Assert.Equal(57, first.StorageSlotIndex);
        var moved = await s.TakeAsync(first, null, Ct); await s.PutAsync(Main, 58, moved, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.TakeAsync(first, null, Ct).AsTask());
        await tx.CommitAsync(Ct);
    }
    [Fact]
    public async Task AvatarArraysCannotMutateWithoutUpdateAndRollbackRestoresDetails()
    {
        using var db = new Database();
        var original = new ItemSnapshot
        {
            ItemId = 500,
            LegacyKind = 8,
            Type = ItemTraits.Avatar,
            InstanceValue = 12345,
            AvatarDetail = new(17, 18, new byte[30], 19, 20, 21),
            TailUnknown0 = 431
        };
        await Seed(db, original);
        var tx = new DomainTransaction(); var s = db.Store.SessionIn(tx); var reference = (await s.GetAsync(Main, 10, Ct))!;
        reference.Snapshot.AvatarDetail!.JewelSocket[0] = 99;
        Assert.Equal(0, (await s.GetAsync(Main, 10, Ct))!.Snapshot.AvatarDetail!.JewelSocket[0]);
        await s.UpdateAsync(reference, x => x with { AvatarDetail = x.AvatarDetail! with { Color1 = 44 } }, Ct);
        var record = await s.TakeAsync(reference, null, Ct); record.Snapshot.AvatarDetail!.JewelSocket[0] = 77;
        await s.PutAsync(new("account:6:AccountCargo"), 0, record, Ct);
        await tx.RollbackAsync();
        var check = new DomainTransaction(); var loaded = (await db.Store.SessionIn(check).GetAsync(Main, 10, Ct))!.Snapshot;
        Assert.Equal(original.ToCore(), loaded.ToCore()); Assert.Equal(19, loaded.AvatarDetail!.Color1);
        await check.RollbackAsync();
    }
    [Fact]
    public async Task CreatureIdentityAndAllDetailFieldsSurviveAccountTransfer()
    {
        using var db = new Database();
        var original = new ItemSnapshot
        {
            ItemId = 600,
            LegacyKind = 5,
            Type = ItemTraits.Creature,
            CreatureDetail = new(91, 1, 234, 3, 4, 5, [6, 7, 8], 9),
            TailUnknown3 = 88
        };
        await Seed(db, original);
        var tx = new DomainTransaction(); var s = db.Store.SessionIn(tx); var source = (await s.GetAsync(Main, 10, Ct))!;
        var record = await s.TakeAsync(source, null, Ct);
        var cargo = await s.PutAsync(new("account:6:AccountCargo"), 0, record, Ct);
        var destination = await s.PutAsync(new("character:61:Main"), 12, await s.TakeAsync(cargo, null, Ct), Ct);
        Assert.Equal(source.ItemUid, destination.ItemUid);
        Assert.Equal(source.ItemUid!.Value.Value, destination.Snapshot.InstanceValue);
        Assert.Equal(original.CreatureDetail!.CreatureText, destination.Snapshot.CreatureDetail!.CreatureText);
        Assert.Equal(234, destination.Snapshot.CreatureDetail.ProgressValue);
        await tx.CommitAsync(Ct);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaterialTransferAcrossDurableAndVolatileHasOneOutcome(bool rollback)
    {
        using var db = new Database(); await Seed(db, Pile(20)); var memory = new VolatileItemDomainStore();
        var tx = new DomainTransaction(); var durable = db.Store.SessionIn(tx); var ephemeral = memory.SessionIn(tx);
        var record = await durable.TakeAsync((await durable.GetAsync(Main, 10, Ct))!, 7, Ct);
        await ephemeral.PutAsync(new("dungeon:1"), 2, record, Ct);
        Assert.Equal(13, (await durable.GetAsync(Main, 10, Ct))!.Count);
        Assert.Equal(7, (await ephemeral.GetAsync(new("dungeon:1"), 2, Ct))!.Count);
        Assert.Equal(1L, await db.Rows(tx));
        if (rollback) await tx.RollbackAsync(); else await tx.CommitAsync(Ct);
        var check = new DomainTransaction();
        Assert.Equal(rollback ? 20 : 13, (await db.Store.SessionIn(check).GetAsync(Main, 10, Ct))!.Count);
        var held = await memory.SessionIn(check).GetAsync(new("dungeon:1"), 2, Ct);
        if (rollback) Assert.Null(held); else Assert.Equal(7, held!.Count);
        await check.RollbackAsync();
    }
    [Fact]
    public async Task VolatileRecordCanBeDeletedViaTransactionScopeOrTransferredBack()
    {
        using var db = new Database(); var memory = new VolatileItemDomainStore(); var tx = new DomainTransaction();
        var ephemeral = memory.SessionIn(tx); var durable = db.Store.SessionIn(tx);
        var piece = await ephemeral.CreateAsync(Pile(10), Ct);
        var (taken, remainder) = await ItemLifecycleScope.In(tx).SplitAsync(piece, 4, Ct);
        await ItemLifecycleScope.In(tx).DeleteItemAsync(taken, Ct);
        await durable.PutAsync(Main, 10, remainder, Ct); await tx.CommitAsync(Ct);
        var check = new DomainTransaction(); Assert.Equal(6, (await db.Store.SessionIn(check).GetAsync(Main, 10, Ct))!.Count);
        await check.RollbackAsync();
    }
    [Fact]
    public async Task ExplicitDeletionRemovesWorldRowAndRejectsRepeatedDeletion()
    {
        using var db = new Database(); await Seed(db, Sword()); var tx = new DomainTransaction(); var session = db.Store.SessionIn(tx);
        var record = await session.TakeAsync((await session.GetAsync(Main, 10, Ct))!, null, Ct);
        Assert.Equal(1L, await db.Rows(tx));
        await ItemLifecycleScope.In(tx).DeleteItemAsync(record, Ct);
        Assert.Equal(0L, await db.Rows(tx));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ItemLifecycleScope.In(tx).DeleteItemAsync(record, Ct).AsTask());
        await tx.CommitAsync(Ct);
    }
    [Fact]
    public async Task PrepareFailureRestoresDurableAndVolatileTransferTogether()
    {
        using var db = new Database(); await Seed(db, Pile(20)); var memory = new VolatileItemDomainStore();
        var tx = new DomainTransaction(); var durable = db.Store.SessionIn(tx); var ephemeral = memory.SessionIn(tx);
        var transferred = await durable.TakeAsync((await durable.GetAsync(Main, 10, Ct))!, 7, Ct);
        await ephemeral.PutAsync(new("dungeon:1"), 2, transferred, Ct);
        _ = await ephemeral.CreateAsync(Pile(2), Ct); // A second unplaced item must abort the entire use case.
        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync(Ct).AsTask());
        var check = new DomainTransaction();
        Assert.Equal(20, (await db.Store.SessionIn(check).GetAsync(Main, 10, Ct))!.Count);
        Assert.Null(await memory.SessionIn(check).GetAsync(new("dungeon:1"), 2, Ct));
        await check.RollbackAsync();
    }
    [Fact]
    public async Task OverflowRefusalDoesNotConsumeIncomingPile()
    {
        using var db = new Database(); await Seed(db, Pile(int.MaxValue)); var tx = new DomainTransaction(); var session = db.Store.SessionIn(tx);
        var incoming = await session.CreateAsync(Pile(2), Ct);
        await Assert.ThrowsAsync<OverflowException>(() => session.PutAsync(Main, 10, incoming, Ct).AsTask());
        await session.PutAsync(Main, 11, incoming, Ct); await tx.CommitAsync(Ct);
    }
    [Fact]
    public async Task NewUnplacedCreatureRollsBackItsUidRowAndReferencesEndWithTransaction()
    {
        using var db = new Database(); var tx = new DomainTransaction(); var session = db.Store.SessionIn(tx);
        var creature = await session.CreateAsync(new ItemSnapshot
        {
            ItemId = 600,
            LegacyKind = 5,
            Type = ItemTraits.Creature,
            CreatureDetail = new(91, 1, 234, 3, 4, 5, [6, 7, 8], 9)
        }, Ct);
        Assert.NotNull(creature.ItemUid);
        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync(Ct).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.PutAsync(Main, 10, creature, Ct).AsTask());
        var check = new DomainTransaction(); _ = db.Store.SessionIn(check);
        Assert.Equal(0L, await db.Rows(check)); await check.RollbackAsync();
    }
    [Fact]
    public async Task EveryLegacyByteSurvivesSnapshotAndStorageRoundTrip()
    {
        var bytes = Enumerable.Range(0, 82).Select(i => (byte)(i * 3 + 1)).ToArray();
        bytes[0] = 1;
        var core = InventoryItemCore.Decode(bytes);
        var snapshot = ItemSnapshot.FromCore(core, false);
        Assert.Equal(bytes, InventoryItemCore.Encode(snapshot.ToCore()));
        Assert.Equal(1, snapshot.Count); Assert.Equal(core.Value, snapshot.InstanceValue);
        using var db = new Database(); await Seed(db, snapshot);
        var tx = new DomainTransaction(); var session = db.Store.SessionIn(tx);
        var item = (await session.GetAsync(Main, 10, Ct))!;
        await session.PutAsync(Main, 11, await session.TakeAsync(item, null, Ct), Ct);
        Assert.Equal(bytes, InventoryItemCore.Encode((await session.GetAsync(Main, 11, Ct))!.Snapshot.ToCore()));
        await tx.RollbackAsync();
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AvatarWithoutPositiveLegacyIdentityCannotCreateAnOrphan(int value)
    {
        using var db = new Database(); var tx = new DomainTransaction(); var session = db.Store.SessionIn(tx);
        var avatar = new ItemSnapshot { ItemId = 500, LegacyKind = 8, Type = ItemTraits.Avatar,
            InstanceValue = value, AvatarDetail = new(0, 0, new byte[30], 0, 0, 0) };
        await Assert.ThrowsAsync<InvalidDataException>(() => session.CreateAsync(avatar, Ct).AsTask());
        Assert.Equal(0L, await db.Rows(tx)); await tx.RollbackAsync();
    }
    [Fact]
    public async Task RevivalCounterCanReachZeroMergeZeroReloadAndRefill()
    {
        using var db = new Database();
        var counter = new ItemSnapshot { ItemId = 1, LegacyKind = 0, Type = ItemTraits.None, IsPile = true, Count = 1 };
        await Seed(db, counter, 1);
        var tx = new DomainTransaction(); var session = db.Store.SessionIn(tx);
        var zero = await session.UpdateAsync((await session.GetAsync(Main, 1, Ct))!, x => x with { Count = 0 }, Ct);
        Assert.Equal(0, zero.Snapshot.ToCore().Value);
        await session.PutAsync(Main, 1, await session.CreateAsync(counter with { Count = 0 }, Ct), Ct);
        await tx.CommitAsync(Ct);
        var reload = new DomainTransaction(); session = db.Store.SessionIn(reload);
        zero = (await session.GetAsync(Main, 1, Ct))!; Assert.Equal(0, zero.Count);
        await session.PutAsync(Main, 1, await session.CreateAsync(counter with { Count = 10 }, Ct), Ct);
        Assert.Equal(10, (await session.GetAsync(Main, 1, Ct))!.Count);
        await reload.CommitAsync(Ct);
    }
    [Fact]
    public async Task LegacyCargoAvatarWithoutDetailIsReadableButCannotBeReissued()
    {
        using var db = new Database(); var tx = new DomainTransaction(); var session = db.Store.SessionIn(tx);
        var avatar = new ItemSnapshot { ItemId = 500, LegacyKind = 8, Type = ItemTraits.Avatar,
            InstanceValue = 5151, AvatarDetail = new(0, 0, new byte[30], 0, 0, 0) };
        var cargo = new ContainerId("account:6:AccountCargo");
        await session.PutAsync(cargo, 0, await session.CreateAsync(avatar, Ct), Ct);
        var owner = tx.Enlist<SqliteDatabaseParticipant>(db.Store.StoreIdentity, () => throw new InvalidOperationException());
        await using (var command = await owner.CommandAsync(Ct))
        {
            command.CommandText = "UPDATE items SET avatar_expire_date=NULL,avatar_clear_id=NULL,avatar_jewel_socket=NULL,avatar_color1=NULL,avatar_color2=NULL,avatar_delete_date=NULL";
            await command.ExecuteNonQueryAsync(Ct);
        }
        var legacy = (await session.GetAsync(cargo, 0, Ct))!;
        Assert.Null(legacy.Snapshot.AvatarDetail); Assert.Equal(5151, legacy.Snapshot.InstanceValue);
        var detached = await session.TakeAsync(legacy, null, Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => session.PutAsync(Main, 10, detached, Ct).AsTask());
        await session.DeleteItemAsync(detached, Ct); await tx.CommitAsync(Ct);
    }
    private sealed class Database : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"item-lifecycle-{Guid.NewGuid():N}.db");
        public SqliteItemDomainStore Store { get; }
        public Database()
        {
            using var connection = new SqliteConnection($"Data Source={path}"); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = """
                CREATE TABLE IF NOT EXISTS items ( item_uid INTEGER PRIMARY KEY AUTOINCREMENT CONSTRAINT world_identity_fits_the_wire CHECK (item_uid BETWEEN 1 AND 2147483647), item_core BLOB NOT NULL CHECK (length(item_core) = 82), cargo_character_id INTEGER, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, avatar_expire_date INTEGER, avatar_clear_id INTEGER, avatar_jewel_socket BLOB CHECK (avatar_jewel_socket IS NULL OR length(avatar_jewel_socket) = 30), avatar_color1 INTEGER, avatar_color2 INTEGER, avatar_delete_date INTEGER, creature_field04 INTEGER, creature_mode_flag INTEGER, creature_progress_value INTEGER, creature_mode1_field0a INTEGER, creature_mode1_field0b INTEGER, creature_field_after_value INTEGER, creature_text BLOB, creature_tail_flag INTEGER );
                CREATE TABLE IF NOT EXISTS item_slots ( owner_scope TEXT NOT NULL CHECK (owner_scope IN ('character', 'account')), owner_id INTEGER NOT NULL, list_type INTEGER NOT NULL, slot_index INTEGER NOT NULL, kind TEXT NOT NULL CHECK (kind IN ('world', 'template')), id INTEGER NOT NULL, core_uid INTEGER NOT NULL, count INTEGER NOT NULL, UNIQUE(owner_scope, owner_id, list_type, slot_index), CONSTRAINT slot_count_is_positive CHECK (count > 0), CONSTRAINT world_slot_holds_exactly_one CHECK (kind <> 'world' OR count = 1), CONSTRAINT world_slot_names_its_own_row CHECK (kind <> 'world' OR core_uid = id) );
                """; command.ExecuteNonQuery();
            Store = new SqliteItemDomainStore(path);
        }
        public async Task<long> Rows(DomainTransaction tx)
        {
            var owner = tx.Enlist<SqliteDatabaseParticipant>(Store.StoreIdentity, () => throw new InvalidOperationException());
            await using var command = await owner.CommandAsync(Ct); command.CommandText = "SELECT COUNT(*) FROM items";
            return Convert.ToInt64(await command.ExecuteScalarAsync(Ct));
        }
        public void Dispose() { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
