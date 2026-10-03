using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

public sealed class SqliteEntryInventoryMutationRepositoryTests
{
    [Fact]
    public async Task ExplicitAvatarChangesPersistAllDetailsAndRollBackAnIdentityCollision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            var side = new InventorySide(path, new NamedAvatars());
            var repository = side.PerCall;
            var main = ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Main);
            await SeedCharacterAsync(path, 6, 60);
            await SeedCharacterAsync(path, 7, 70);
            await SeedAvatarAsync(path, 60, 9, 100, 1000, new(1, 2, new byte[30], 3, 4, 5));
            await SeedAvatarAsync(path, 60, 10, 200, 2000, new(6, 7, new byte[30], 8, 9, 10));
            await SeedAvatarAsync(path, 70, 9, 900, 9000, new(11, 12, new byte[30], 13, 14, 15));
            var updatedDetail = new InventoryAvatarDetailState(21, 22, Enumerable.Repeat((byte)23, 30).ToArray(), 24, 25, 26);
            var insertedDetail = new InventoryAvatarDetailState(31, 32, Enumerable.Repeat((byte)33, 30).ToArray(), 34, 35, 36);
            ItemIdentity? originalUid = null;
            await side.InUseCaseAsync(async token =>
            {
                var items = side.Store.SessionIn(side.Scope.Current!);
                var source = (await items.GetAsync(main, 9, token))!;
                originalUid = source.ItemUid;
                source = await items.UpdateAsync(source, snapshot => snapshot with { AvatarDetail = updatedDetail }, token);
                await items.PutAsync(main, 11, await items.TakeAsync(source, null, token), token);
                await items.DeleteItemAsync(await items.TakeAsync((await items.GetAsync(main, 10, token))!, null, token), token);
                var added = await side.Lifecycle.CreateAsync(ItemSnapshot.FromCore(
                    StackableItemCore.Create(8, 300, 3000), false, avatar: insertedDetail), token);
                await items.PutAsync(main, 12, added, token);
                return true;
            });
            var saved = await repository.LoadCharacterAsync(new(6), new(60), default);
            Assert.Equal([1000, 3000], saved.CharacterItems.Select(item => item.Snapshot.InstanceValue).Order());
            var moved = Assert.Single(saved.CharacterItems, item => item.Snapshot.InstanceValue == 1000);
            Assert.Equal(originalUid, moved.ItemUid);
            Assert.Equal(11, moved.SlotIndex);
            Assert.Equivalent(updatedDetail, moved.Snapshot.AvatarDetail);
            Assert.Equivalent(insertedDetail, Assert.Single(saved.CharacterItems, item => item.Snapshot.InstanceValue == 3000).Snapshot.AvatarDetail);
            await using (var connection = Open(path))
                Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM items WHERE avatar_expire_date IS NOT NULL AND substr(item_core,6,4)=X'D0070000'"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => side.InUseCaseAsync(async token =>
            {
                var items = side.Store.SessionIn(side.Scope.Current!);
                // Make an earlier valid deletion so the collision also proves transaction rollback.
                await items.DeleteItemAsync(await items.TakeAsync((await items.GetAsync(main, 11, token))!, null, token), token);
                var duplicate = await side.Lifecycle.CreateAsync(ItemSnapshot.FromCore(
                    StackableItemCore.Create(8, 900, 9000), false, avatar: insertedDetail), token);
                await items.PutAsync(main, 13, duplicate, token);
                return true;
            }));
            var restored = await repository.LoadCharacterAsync(new(6), new(60), default);
            Assert.Equal([1000, 3000], restored.CharacterItems.Select(item => item.Snapshot.InstanceValue).Order());
            Assert.Equal(originalUid, Assert.Single(restored.CharacterItems, item => item.Snapshot.InstanceValue == 1000).ItemUid);
            Assert.Equivalent(updatedDetail, Assert.Single(restored.CharacterItems, item => item.Snapshot.InstanceValue == 1000).Snapshot.AvatarDetail);
            var other = await repository.LoadCharacterAsync(new(7), new(70), default);
            Assert.Equal(15, Assert.Single(other.CharacterItems).Snapshot.AvatarDetail!.DeleteDate);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task UpdateCannotRewriteAnAvatarToAnotherCharactersIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            var side = new InventorySide(path, new NamedAvatars());
            await SeedCharacterAsync(path, 6, 60);
            await SeedCharacterAsync(path, 7, 70);
            await SeedAvatarAsync(path, 60, 9, 100, 1000, new(1, 2, new byte[30], 3, 4, 5));
            await SeedAvatarAsync(path, 70, 9, 900, 9000, new(11, 12, new byte[30], 13, 14, 15));
            var main = ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Main);
            await Assert.ThrowsAsync<InvalidOperationException>(() => side.InUseCaseAsync(async token =>
            {
                var items = side.Store.SessionIn(side.Scope.Current!);
                var mine = (await items.GetAsync(main, 9, token))!;
                await items.UpdateAsync(mine, item => item with
                {
                    InstanceValue = 9000,
                    AvatarDetail = new InventoryAvatarDetailState(41, 42, Enumerable.Repeat((byte)43, 30).ToArray(), 44, 45, 46),
                }, token);
                return true;
            }));
            var mineAfter = Assert.Single((await side.PerCall.LoadCharacterAsync(new(6), new(60), default)).CharacterItems);
            Assert.Equal(1000, mineAfter.Snapshot.InstanceValue);
            Assert.Equal(5, mineAfter.Snapshot.AvatarDetail!.DeleteDate);
            var otherAfter = Assert.Single((await side.PerCall.LoadCharacterAsync(new(7), new(70), default)).CharacterItems);
            Assert.Equal(9000, otherAfter.Snapshot.InstanceValue);
            Assert.Equal(15, otherAfter.Snapshot.AvatarDetail!.DeleteDate);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task ExplicitDeleteMoveUpdateAndCreateCommitTogetherAndWrongOwnerRefillChangesNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            var side = new InventorySide(path);
            await SeedCharacterAsync(path, 6, 60);
            await SeedItemAsync(path, 60, 65, 100, 1);
            await SeedItemAsync(path, 60, 66, 200, 2);
            var main = ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Main);
            await side.InUseCaseAsync(async token =>
            {
                var items = side.Store.SessionIn(side.Scope.Current!);
                var first = (await items.GetAsync(main, 65, token))!;
                first = await items.UpdateAsync(first, snapshot => snapshot with { Count = 9 }, token);
                await items.PutAsync(main, 70, await items.TakeAsync(first, null, token), token);
                await items.DeleteItemAsync(await items.TakeAsync((await items.GetAsync(main, 66, token))!, null, token), token);
                await items.PutAsync(main, 71, await side.Lifecycle.CreateAsync(
                    ItemSnapshot.FromCore(StackableItemCore.Create(2, 300, 3), true), token), token);
                return true;
            });
            var saved = await side.PerCall.LoadCharacterAsync(new(6), new(60), default);
            Assert.Equal([100, 300], saved.CharacterItems.Select(item => item.ItemId).Order());
            var updated = Assert.Single(saved.CharacterItems, item => item.ItemId == 100);
            Assert.Equal(70, updated.SlotIndex);
            Assert.Equal(9, updated.Count);
            // Ownership belongs to the use-case entry boundary; the low-level item store has no account parameter.
            side.BindMain(60);
            var rule = new DailyRefillRule(400, 3, DateTimeOffset.MaxValue, DailyRefillMode.RefillToTarget,
                100, 2, new(BusinessInventorySpace.Consumable, 0, 55));
            await Assert.ThrowsAsync<InvalidDataException>(() => side.MutationsPerCall.ApplyDailyRefillAsync(
                new(999), new(60), [rule], PreparedAt, default).AsTask());
            var after = await side.PerCall.LoadCharacterAsync(new(6), new(60), default);
            Assert.Equal([(70, 100, 9), (71, 300, 3)], after.CharacterItems.Select(item => (item.SlotIndex, item.ItemId, item.Count)));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task DailyRefill_CommitsGrantAndClaimTogetherAndSecondRunIsNoOp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            await SeedCharacterAsync(path, accountId: 7, characterId: 70);
            var side = new InventorySide(path);
            side.BindMain(70);
            var repository = side.MutationsPerCall;
            var rule = new DailyRefillRule(1234, 5, DateTimeOffset.MaxValue,
                DailyRefillMode.RefillToTarget, 100, 2,
                new(BusinessInventorySpace.Consumable, 0, 55));

            var first = await repository.ApplyDailyRefillAsync(
                new AccountId(7), new CharacterId(70), [rule], PreparedAt, CancellationToken.None);
            var second = await repository.ApplyDailyRefillAsync(
                new AccountId(7), new CharacterId(70), [rule], PreparedAt, CancellationToken.None);

            var grant = Assert.Single(first.Grants);
            Assert.Equal(new BusinessSlot(BusinessInventorySpace.Consumable, 0), grant.Slot);
            Assert.Equal(5, grant.GrantedCount);
            Assert.Equal(5, grant.CommittedCount);
            Assert.Empty(second.Grants);
            await using var connection = Open(path);
            Assert.Equal(5, await ReadItemCountAsync(connection, 70, 1234));
            Assert.Equal(2L, await ScalarAsync(connection,
                "SELECT hex(substr(held.item_core,1,1)) "
                + "FROM item_slots place JOIN items held ON held.item_uid=place.core_uid WHERE place.owner_id=70"));
            Assert.Equal("FFFFFFFF", await ScalarStringAsync(connection,
                "SELECT hex(substr(held.item_core,22,4)) "
                + "FROM item_slots place JOIN items held ON held.item_uid=place.core_uid WHERE place.owner_id=70"));
            Assert.Equal(1L, await ScalarAsync(connection,
                "SELECT value FROM character_daily_counters WHERE character_id=70 AND counter_key='pvf_daily_refill_item_1234'"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    /// <summary>
    /// A move is readable through the tables inside the same use case, exactly as a placement is.
    /// </summary>
    /// <remarks>
    /// The refill is the only caller that writes and then reads this way today, so it is the only
    /// one whose loss would have shown up. A move goes through the same landing rule and would fail
    /// the same way the first time anything read it back -- an inventory delta, a snapshot, a
    /// second command in one use case -- so it is pinned here rather than left to whoever writes
    /// that caller.
    ///
    /// A swap is used rather than a plain relocation because it is the verb with the most writes:
    /// two slots cleared and two filled, all inside one batch.
    /// </remarks>
    [Fact]
    public async Task AMoveIsVisibleToTheTablesInsideTheSameUseCase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            await SeedCharacterAsync(path, accountId: 7, characterId: 70);
            await SeedItemAsync(path, characterId: 70, slot: 65, itemId: 1234, count: 3);
            await SeedItemAsync(path, characterId: 70, slot: 66, itemId: 5678, count: 1);
            var side = new InventorySide(path);
            side.BindMain(70);
            var bag = side.Containers.GetContainer(
                new CharacterId(70), InventoryContainerType.Main);
            var repository = side.Mutations;

            var seen = await side.InUseCaseAsync(async token =>
            {
                var outcome = await side.MoveAsync(new CharacterId(70),
                    new(BusinessInventorySpace.Consumable, 0),
                    new(BusinessInventorySpace.Consumable, 1), 0, token).ConfigureAwait(false);
                Assert.IsType<ItemMoveOutcome.Moved>(outcome);
                return await side.ReadMainItemsAsync(new AccountId(7), new CharacterId(70), token)
                    .ConfigureAwait(false);
            });

            Assert.Equal(
                [(65, 5678, 1), (66, 1234, 3)],
                seen.Select(item => (item.SlotIndex, item.Core.ItemId, item.Core.Value)).ToArray());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    /// <summary>
    /// Inside one use case, a reader that does not share the bag's session still sees what the
    /// refill placed.
    /// </summary>
    /// <remarks>
    /// This is the shape a real entry has and the one every other refill test here does not: the
    /// server dispatches the refill as one command, and that same command reloads the inventory
    /// through the tables so it can tell the client what changed. A bag holds its writes until the
    /// use case settles, so without the refill saying that its decisions are to be applied, the
    /// reload is handed the inventory as it stood before -- stale counts on a top-up day, and on a
    /// day that opens a slot a reload that cannot find the row it was told about and fails the
    /// whole entry.
    ///
    /// Every other test in this file drives the refill through `MutationsPerCall`, which puts a use
    /// case around each call separately. Under that shape the two write channels were
    /// indistinguishable, which is exactly why nothing went red when they stopped being so.
    /// </remarks>
    [Theory]
    [InlineData(0, 65, 5)]
    [InlineData(3, 65, 5)]
    public async Task DailyRefill_IsVisibleToTheEntryReloadInsideTheSameUseCase(
        int seeded, int expectedSlot, int expectedCount)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            await SeedCharacterAsync(path, accountId: 7, characterId: 70);
            if (seeded > 0)
            {
                await SeedItemAsync(path, characterId: 70, slot: 65, itemId: 1234, count: seeded);
            }

            var side = new InventorySide(path);
            side.BindMain(70);
            var repository = side.Mutations;
            var rule = new DailyRefillRule(1234, 5, DateTimeOffset.MaxValue,
                DailyRefillMode.RefillToTarget, 100, 2,
                new(BusinessInventorySpace.Consumable, 0, 55));

            var reloaded = await side.InUseCaseAsync(async token =>
            {
                await repository.ApplyDailyRefillAsync(
                    new AccountId(7), new CharacterId(70), [rule], PreparedAt, token)
                    .ConfigureAwait(false);
                return await side.ReadMainItemsAsync(new AccountId(7), new CharacterId(70), token)
                    .ConfigureAwait(false);
            });

            var item = Assert.Single(reloaded);
            Assert.Equal(expectedSlot, item.SlotIndex);
            Assert.Equal(1234, item.Core.ItemId);
            Assert.Equal(expectedCount, item.Core.Value);
            // The archive this side is given declares every item an ordinary consumable, so what
            // came back is a pile, and a pile is reloaded with no name to carry.
            Assert.Null(item.Identity);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    /// <summary>Refilling updates the count in place while preserving slot, template and backing row.</summary>
    [Fact]
    public async Task DailyRefill_UpdatesTheCountInPlaceAndPreservesTheSlot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            await SeedCharacterAsync(path, accountId: 7, characterId: 70);
            await SeedItemAsync(path, characterId: 70, slot: 65, itemId: 1234, count: 3);
            var side = new InventorySide(path);
            side.BindMain(70);
            var repository = side.MutationsPerCall;
            var rule = new DailyRefillRule(1234, 5, DateTimeOffset.MaxValue,
                DailyRefillMode.RefillToTarget, 100, 2,
                new(BusinessInventorySpace.Consumable, 0, 55));

            long keyBefore;
            await using (var before = Open(path))
            {
                keyBefore = await ScalarAsync(
                    before,
                    "SELECT core_uid FROM item_slots WHERE owner_id=70 AND slot_index=65");
            }

            await repository.ApplyDailyRefillAsync(
                new AccountId(7), new CharacterId(70), [rule], PreparedAt, CancellationToken.None);

            var projected = await side.ReadMainItemsAsync(new AccountId(7),
                new CharacterId(70), CancellationToken.None);
            var item = Assert.Single(projected);
            Assert.Equal(65, item.SlotIndex);
            Assert.Equal(1234, item.Core.ItemId);
            Assert.Equal(5, item.Core.Value);
            Assert.Equal(
                projected.Select(value => value.SlotIndex).Distinct().Count(), projected.Count);

            await using var after = Open(path);
            var keyAfter = await ScalarAsync(
                after,
                "SELECT core_uid FROM item_slots WHERE owner_id=70 AND slot_index=65");
            Assert.Equal(1L, await ScalarAsync(
                after, "SELECT COUNT(*) FROM item_slots WHERE owner_id=70"));
            Assert.Equal(keyBefore, keyAfter);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    /// <summary>
    /// A rule whose archive entry declares no item kind cannot open a slot, and fails the entry
    /// rather than storing a row the entry path cannot read back.
    /// </summary>
    /// <remarks>
    /// Nothing asserted this before. The check lived in the refill, in the branch that opened a
    /// slot, and it moved into the bag with the rest of the landing -- so it is exactly the kind of
    /// rule a migration drops without anything going red.
    ///
    /// Only opening a slot needs the kind. Joining a pile does not, because the pile already
    /// carries one, and that asymmetry is pinned by the second half of this test.
    /// </remarks>
    [Fact]
    public async Task DailyRefill_RefusesToOpenASlotForARuleWithNoItemKindButStillJoinsAPile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            await SeedCharacterAsync(path, accountId: 7, characterId: 70);
            var side = new InventorySide(path);
            side.BindMain(70);
            var repository = side.MutationsPerCall;
            var kindless = new DailyRefillRule(1234, 5, DateTimeOffset.MaxValue,
                DailyRefillMode.RefillToTarget, 100, 0,
                new(BusinessInventorySpace.Consumable, 0, 55));

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.ApplyDailyRefillAsync(
                    new AccountId(7), new CharacterId(70), [kindless], PreparedAt,
                    CancellationToken.None));

            await using (var refused = Open(path))
            {
                Assert.Equal(0L, await ScalarAsync(
                    refused, "SELECT COUNT(*) FROM item_slots WHERE owner_id=70"));
                Assert.Equal(0L, await ScalarAsync(
                    refused,
                    "SELECT COUNT(*) FROM character_daily_counters WHERE character_id=70"));
            }

            await SeedItemAsync(path, characterId: 70, slot: 65, itemId: 1234, count: 3);
            var joined = await repository.ApplyDailyRefillAsync(
                new AccountId(7), new CharacterId(70), [kindless], PreparedAt,
                CancellationToken.None);

            var grant = Assert.Single(joined.Grants);
            Assert.Equal(new BusinessSlot(BusinessInventorySpace.Consumable, 0), grant.Slot);
            Assert.Equal(2, grant.GrantedCount);
            Assert.Equal(5, grant.CommittedCount);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task DailyRefill_ClaimsZeroGrantButOwnershipFailureRollsBackAll()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            await SeedCharacterAsync(path, 8, 80);
            await SeedItemAsync(path, 80, 65, 2222, 5);
            var side = new InventorySide(path);
            side.BindMain(80);
            var repository = side.MutationsPerCall;
            var rule = new DailyRefillRule(2222, 2, DateTimeOffset.MaxValue,
                DailyRefillMode.RefillToTarget, 100, 2,
                new(BusinessInventorySpace.Consumable, 0, 55));

            var result = await repository.ApplyDailyRefillAsync(
                new AccountId(8), new CharacterId(80), [rule], PreparedAt, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.ApplyDailyRefillAsync(
                    new AccountId(999), new CharacterId(80),
                    [rule with { ItemId = 3333 }], PreparedAt, CancellationToken.None));

            Assert.Empty(result.Grants);
            await using var connection = Open(path);
            Assert.Equal(1L, await ScalarAsync(connection,
                "SELECT value FROM character_daily_counters WHERE character_id=80 AND counter_key='pvf_daily_refill_item_2222'"));
            Assert.Equal(0L, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM character_daily_counters WHERE counter_key='pvf_daily_refill_item_3333'"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    /// <summary>
    /// The day a refill belongs to is the one in UTC+8, not the one in UTC.
    /// </summary>
    /// <remarks>
    /// Two entries an hour apart across midnight in Seoul are two days and refill twice; two
    /// entries either side of midnight in UTC are one day and refill once. Nothing else in the
    /// suite pins which of the two the server means, so a boundary quietly moved to UTC would give
    /// every player a second refill at 16:00 local and nothing would go red.
    /// </remarks>
    [Fact]
    public async Task DailyRefill_TurnsOverAtMidnightInUtcPlusEight()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            await SeedCharacterAsync(path, accountId: 7, characterId: 70);
            var side = new InventorySide(path);
            side.BindMain(70);
            var repository = side.MutationsPerCall;
            // Adding up to the stack limit rather than refilling to a target, so that a second
            // day grants again -- a refill-to-target rule would grant nothing the second day
            // whatever the boundary did, and the test could not tell the two apart.
            var rule = new DailyRefillRule(1234, 5, DateTimeOffset.MaxValue,
                DailyRefillMode.AddUpToStackLimit, 100, 2,
                new(BusinessInventorySpace.Consumable, 0, 55));

            // 23:30 and 00:30 in UTC+8: one hour apart, and two different days.
            var lateOnDayOne = new DateTimeOffset(2026, 8, 12, 23, 30, 0, TimeSpan.FromHours(8));
            var earlyOnDayTwo = new DateTimeOffset(2026, 8, 13, 0, 30, 0, TimeSpan.FromHours(8));
            var first = await repository.ApplyDailyRefillAsync(
                new AccountId(7), new CharacterId(70), [rule], lateOnDayOne, CancellationToken.None);
            var second = await repository.ApplyDailyRefillAsync(
                new AccountId(7), new CharacterId(70), [rule], earlyOnDayTwo, CancellationToken.None);

            Assert.Single(first.Grants);
            Assert.Single(second.Grants);

            // The same two instants written as UTC straddle midnight in UTC as well -- but the
            // other way round, so a server that used UTC would see one day where this sees two.
            Assert.Equal(12, lateOnDayOne.UtcDateTime.Day);
            Assert.Equal(12, earlyOnDayTwo.UtcDateTime.Day);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    /// <remarks>
    /// The pair to the one above: an entry late in the UTC+8 day and one early the next morning in
    /// UTC are the same UTC+8 day, and refill once between them.
    /// </remarks>
    [Fact]
    public async Task DailyRefill_TreatsOneUtcPlusEightDayAsOneDayHoweverUtcFalls()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            await SeedCharacterAsync(path, accountId: 7, characterId: 70);
            var side = new InventorySide(path);
            side.BindMain(70);
            var repository = side.MutationsPerCall;
            var rule = new DailyRefillRule(1234, 5, DateTimeOffset.MaxValue,
                DailyRefillMode.AddUpToStackLimit, 100, 2,
                new(BusinessInventorySpace.Consumable, 0, 55));

            // 07:30 and 08:30 in UTC+8 on one day: 23:30 and 00:30 in UTC, so two UTC days.
            var beforeUtcMidnight = new DateTimeOffset(2026, 8, 13, 7, 30, 0, TimeSpan.FromHours(8));
            var afterUtcMidnight = new DateTimeOffset(2026, 8, 13, 8, 30, 0, TimeSpan.FromHours(8));
            Assert.Equal(12, beforeUtcMidnight.UtcDateTime.Day);
            Assert.Equal(13, afterUtcMidnight.UtcDateTime.Day);

            var first = await repository.ApplyDailyRefillAsync(
                new AccountId(7), new CharacterId(70), [rule], beforeUtcMidnight, CancellationToken.None);
            var second = await repository.ApplyDailyRefillAsync(
                new AccountId(7), new CharacterId(70), [rule], afterUtcMidnight, CancellationToken.None);

            Assert.Single(first.Grants);
            Assert.Empty(second.Grants);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    /// <summary>
    /// A character who already holds some is topped up in place; one who holds none gets a slot.
    /// </summary>
    /// <remarks>
    /// The two branches end in different rows and different acknowledgements, and only the count in
    /// the existing pile tells them apart. A decision that always opened a new slot would leave the
    /// player with two piles of potions and the client drawing both.
    /// </remarks>
    [Fact]
    public async Task DailyRefill_AddsToAPileThatExistsRatherThanOpeningASecond()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            await SeedCharacterAsync(path, accountId: 7, characterId: 70);
            await SeedItemAsync(path, characterId: 70, slot: 70, itemId: 1234, count: 2);
            var side = new InventorySide(path);
            side.BindMain(70);
            var repository = side.MutationsPerCall;
            var rule = new DailyRefillRule(1234, 5, DateTimeOffset.MaxValue,
                DailyRefillMode.RefillToTarget, 100, 2,
                new(BusinessInventorySpace.Consumable, 0, 55));

            var result = await repository.ApplyDailyRefillAsync(
                new AccountId(7), new CharacterId(70), [rule], PreparedAt, CancellationToken.None);

            var grant = Assert.Single(result.Grants);
            Assert.Equal(new BusinessSlot(BusinessInventorySpace.Consumable, 5), grant.Slot);
            Assert.Equal(3, grant.GrantedCount);
            Assert.Equal(5, grant.CommittedCount);
            await using var connection = Open(path);
            Assert.Equal(1L, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM item_slots WHERE owner_id=70"));
            Assert.Equal(5, await ReadItemCountAsync(connection, 70, 1234));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    /// <summary>
    /// A new pile goes to the first free slot of the rule's own band, not to the first free slot.
    /// </summary>
    /// <remarks>
    /// Slots below the band belong to equipment and the quick bar. An item placed there is one the
    /// client draws somewhere the player does not look for it, and the search skipping occupied
    /// slots is what keeps two refills on one entry from landing on each other.
    /// </remarks>
    [Fact]
    public async Task DailyRefill_TakesTheFirstFreeSlotOfTheRulesOwnBand()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            await SeedCharacterAsync(path, accountId: 7, characterId: 70);
            // Slot 3 is free and below the band; 65 and 66 are taken, so 67 is the answer.
            await SeedItemAsync(path, characterId: 70, slot: 65, itemId: 9001, count: 1);
            await SeedItemAsync(path, characterId: 70, slot: 66, itemId: 9002, count: 1);
            var side = new InventorySide(path);
            side.BindMain(70);
            var repository = side.MutationsPerCall;
            var rule = new DailyRefillRule(1234, 5, DateTimeOffset.MaxValue,
                DailyRefillMode.RefillToTarget, 100, 2,
                new(BusinessInventorySpace.Consumable, 0, 55));

            var result = await repository.ApplyDailyRefillAsync(
                new AccountId(7), new CharacterId(70), [rule], PreparedAt, CancellationToken.None);

            Assert.Equal(new BusinessSlot(BusinessInventorySpace.Consumable, 2), Assert.Single(result.Grants).Slot);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    /// <remarks>
    /// A band with nothing free is refused rather than spilling into the next one. The refusal
    /// takes the whole entry with it, which is the loud failure the alternative -- an item quietly
    /// placed outside its band -- would not be.
    /// </remarks>
    [Fact]
    public async Task DailyRefill_RefusesWhenTheBandIsFull()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-entry-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            await SeedCharacterAsync(path, accountId: 7, characterId: 70);
            await SeedItemAsync(path, characterId: 70, slot: 65, itemId: 9001, count: 1);
            await SeedItemAsync(path, characterId: 70, slot: 66, itemId: 9002, count: 1);
            var side = new InventorySide(path);
            side.BindMain(70);
            var repository = side.MutationsPerCall;
            var rule = new DailyRefillRule(1234, 5, DateTimeOffset.MaxValue,
                DailyRefillMode.RefillToTarget, 100, 2,
                new(BusinessInventorySpace.Consumable, 0, 1));

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.ApplyDailyRefillAsync(
                    new AccountId(7), new CharacterId(70), [rule], PreparedAt, CancellationToken.None));

            await using var connection = Open(path);
            Assert.Equal(0L, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM character_daily_counters WHERE character_id=70"));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    private static readonly DateTimeOffset PreparedAt =
        new(2026, 8, 12, 10, 0, 0, TimeSpan.FromHours(8));

    /// <summary>The templates this fixture declares avatars; everything else an ordinary pile.</summary>
    private sealed class NamedAvatars : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(itemId is 100 or 200 or 300 or 900
                ? new ItemPlacement(8, 1, new(BusinessInventorySpace.Avatar, 0, 255))
                    { WorldClass = ItemWorldClass.Avatar }
                : new ItemPlacement(2, 100, new(BusinessInventorySpace.Consumable, 0, 55)));
    }

    private static async Task SeedCharacterAsync(string path, int accountId, int characterId)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO characters(character_id,account_id,name) VALUES(@cid,@aid,'entry')";
        command.Parameters.AddWithValue("@cid", characterId); command.Parameters.AddWithValue("@aid", accountId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedItemAsync(string path, int characterId, int slot, int itemId, int count)
    {
        var core = new byte[82]; core[0] = 2; core[66] = byte.MaxValue;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1, 4), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5, 4), count);
        await using var connection = Open(path); await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO items(item_core) VALUES(@core);"
            + "INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) "
            + "VALUES('character',@cid,0,@slot,'template',@id,"
            + "last_insert_rowid(),@count)";
        command.Parameters.AddWithValue("@cid", characterId); command.Parameters.AddWithValue("@slot", slot);
        command.Parameters.AddWithValue("@id", itemId); command.Parameters.AddWithValue("@count", count);
        command.Parameters.AddWithValue("@core", core); await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedAvatarAsync(
        string path, int characterId, int slot, int itemId, int avatarUid,
        InventoryAvatarDetailState detail)
    {
        var core = new byte[82];
        core[0] = 8;
        core[66] = byte.MaxValue;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1, 4), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5, 4), avatarUid);
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO items(item_core,
              avatar_expire_date,avatar_clear_id,avatar_jewel_socket,avatar_color1,avatar_color2,
              avatar_delete_date)
            VALUES(@core,@expire,@clear,@socket,@color1,@color2,@delete);
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
            VALUES('character',@cid,0,@slot,'world',last_insert_rowid(),last_insert_rowid(),1);
            """;
        command.Parameters.AddWithValue("@cid", characterId);
        command.Parameters.AddWithValue("@slot", slot);
        command.Parameters.AddWithValue("@core", core);
        command.Parameters.AddWithValue("@expire", detail.ExpireDate);
        command.Parameters.AddWithValue("@clear", detail.ClearAvatarId);
        command.Parameters.AddWithValue("@socket", detail.JewelSocket);
        command.Parameters.AddWithValue("@color1", detail.Color1);
        command.Parameters.AddWithValue("@color2", detail.Color2);
        command.Parameters.AddWithValue("@delete", detail.DeleteDate);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReadItemCountAsync(SqliteConnection connection, int characterId, int itemId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT held.item_core FROM item_slots place "
            + "JOIN items held ON held.item_uid=place.core_uid WHERE place.owner_id=@cid";
        command.Parameters.AddWithValue("@cid", characterId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var core = (byte[])reader.GetValue(0);
            if (BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1, 4)) == itemId)
                return BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(5, 4));
        }
        return 0;
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}"); connection.Open(); return connection;
    }
}
