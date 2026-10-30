using Dfo.Server.Application.Transactions;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// Whether a creature's eight facts, and the order they are listed in, belong to the creature or
/// to the position it is standing in.
/// </summary>
/// <remarks>
/// The eight used to be a row of <c>character_creatures</c> keyed <c>(character_id, sort_order)</c>
/// and they are columns of the world row now. Two things about that read have to survive the move,
/// because the creature list frame is written straight out of it:
///
/// <b>The order.</b> It was ascending <c>sort_order</c>. Nothing ever wrote that column after the
/// row was made, so the list a character was sent did not depend on where its creatures were
/// standing and did not change when one of them moved. Ascending <c>item_uid</c> is the same
/// statement said about a column that cannot be rewritten at all: the identity is the one thing a
/// move is defined to preserve. These pin both halves -- that the order is the identity's, and
/// that it survives a move -- so a read ordering on the position instead fails here rather than
/// quietly reordering somebody's list.
///
/// <b>The facts.</b> Whether they follow the creature or the position is invisible from the column
/// list: a move that rewrote them at the destination would look exactly like one that left them
/// alone, and both would answer correctly for a single creature moved on its own. So the
/// discriminating case is two creatures trading places, which is the fourth test here.
///
/// Everything is read back through the entry load rather than out of the columns, because the
/// question is what the character is served and the load is the only thing that turns the columns
/// into that.
/// </remarks>
public sealed class CreatureStateFollowsTheIdentityTests
{
    private const int Account = 44;

    private const int Character = 55;

    /// <summary>What an item core says it is when it is a creature.</summary>
    private const byte CreatureKind = 5;

    private static readonly CharacterId Owner = new(Character);

    private static readonly OwnedCharacterId Place =
        new(new AccountId(Account), Owner);

    private static readonly ContainerId Main =
        ContainerId.ForCharacter(Owner, InventoryContainerType.Main);

    private static readonly ContainerId Equipment =
        ContainerId.ForCharacter(Owner, InventoryContainerType.Equipment);

    /// <summary>
    /// The list runs in ascending identity, and the rank it answers with is dense from zero.
    /// </summary>
    /// <remarks>
    /// The three are seeded so that no other order agrees with this one: their identities ascend
    /// 3, 7, 11 while the positions they stand in run the other way, and they are inserted in a
    /// third order again. A read that ordered on the position would answer 11, 7, 3; one that
    /// ordered on nothing at all would answer in insertion order, 7, 3, 11.
    ///
    /// The rank matters as much as the sequence. The column it replaces was dense from zero for
    /// each character, and the caller sorts on what this answers, so a rank that skipped or
    /// repeated would be a different list.
    /// </remarks>
    [Fact]
    public async Task TheListRunsInAscendingIdentityAndTheRankIsDenseFromZero()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await InsertCreatureAsync(path, identity: 7, listType: 0, slot: 12, which: 1);
            await InsertCreatureAsync(path, identity: 3, listType: 0, slot: 13, which: 2);
            await InsertCreatureAsync(path, identity: 11, listType: 0, slot: 11, which: 3);

            var pets = await CreaturesAsync(side);

            Assert.Equal([3L, 7L, 11L], pets.Select(value => value.CreatureKey));
            Assert.Equal([0, 1, 2], pets.Select(value => value.SortOrder));
        });
    }

    /// <summary>
    /// A creature that moves does not move in the list, and neither does anything else.
    /// </summary>
    /// <remarks>
    /// This is the property the retired column had for free, because nothing wrote it. Three
    /// moves, one of them into the equipment slot a creature is worn in, and the list is read back
    /// after each: same identities, same order, same ranks.
    /// </remarks>
    [Fact]
    public async Task TheListDoesNotReorderWhenACreatureMoves()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await InsertCreatureAsync(path, identity: 7, listType: 0, slot: 12, which: 1);
            await InsertCreatureAsync(path, identity: 3, listType: 0, slot: 13, which: 2);
            await InsertCreatureAsync(path, identity: 11, listType: 0, slot: 11, which: 3);

            var before = await CreaturesAsync(side);
            Assert.Equal([3L, 7L, 11L], before.Select(value => value.CreatureKey));

            foreach (var (from, fromSlot, to, toSlot) in new[]
                     {
                         (Main, 13, Main, 40),
                         (Main, 40, Equipment, 24),
                         (Equipment, 24, Main, 13),
                     })
            {
                await SettleItemsAsync(path, async session =>
                {
                    var item = await session.TakeAsync((await session.GetAsync(from, fromSlot, default))!, null, default);
                    await session.PutAsync(to, toSlot, item, default);
                });

                // The one that moved is still the same row, so the list cannot have gained or lost
                // a member for a reason other than the order.
                Assert.Equal(3L, await IdentityAtAsync(path, to, toSlot));

                var after = await CreaturesAsync(side);
                Assert.Equal(
                    before.Select(value => value.CreatureKey),
                    after.Select(value => value.CreatureKey));
                Assert.Equal([0, 1, 2], after.Select(value => value.SortOrder));
            }
        });
    }

    /// <summary>A creature moved is served with the same eight facts it had before, one by one.</summary>
    /// <remarks>
    /// Every one of the eight carries a value of its own and none of them the default, so a move
    /// that dropped or defaulted any single field fails on that field's name rather than on a
    /// whole record being unequal.
    /// </remarks>
    [Fact]
    public async Task ACreatureKeepsAllEightOfItsFactsFieldByFieldWhenItMoves()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await InsertCreatureAsync(path, identity: 9, listType: 0, slot: 12, which: 1);

            var before = Assert.Single(await CreaturesAsync(side));

            await SettleItemsAsync(path, async session =>
            {
                var item = await session.TakeAsync((await session.GetAsync(Main, 12, default))!, null, default);
                await session.PutAsync(Equipment, 24, item, default);
            });

            Assert.Equal(9L, await IdentityAtAsync(path, Equipment, 24));
            var after = Assert.Single(await CreaturesAsync(side));
            Assert.Equal(before.CreatureKey, after.CreatureKey);
            Assert.Equal(before.Field04, after.Field04);
            Assert.Equal(before.ModeFlag, after.ModeFlag);
            Assert.Equal(before.ProgressValue, after.ProgressValue);
            Assert.Equal(before.Mode1Field0A, after.Mode1Field0A);
            Assert.Equal(before.Mode1Field0B, after.Mode1Field0B);
            Assert.Equal(before.FieldAfterValue, after.FieldAfterValue);
            Assert.Equal(before.CreatureText, after.CreatureText);
            Assert.Equal(before.TailFlag, after.TailFlag);

            // And the seeded values themselves, so that "unchanged" cannot be satisfied by both
            // reads answering with nothing.
            var seeded = Facts(1);
            Assert.Equal(9L, after.CreatureKey);
            Assert.Equal(seeded.Field04, after.Field04);
            Assert.Equal(seeded.ModeFlag, after.ModeFlag);
            Assert.Equal(seeded.ProgressValue, after.ProgressValue);
            Assert.Equal(seeded.Mode1Field0A, after.Mode1Field0A);
            Assert.Equal(seeded.Mode1Field0B, after.Mode1Field0B);
            Assert.Equal(seeded.FieldAfterValue, after.FieldAfterValue);
            Assert.Equal(seeded.CreatureText, after.CreatureText);
            Assert.Equal(seeded.TailFlag, after.TailFlag);
        });
    }

    /// <summary>
    /// Two creatures trading positions each arrive carrying their own facts, not the position's.
    /// </summary>
    /// <remarks>
    /// Facts belonging to the position would leave the creature that arrives at the first position
    /// answering with the facts the other one left there. Facts belonging to the thing arrive with
    /// the thing, so each identity answers with its own however the two are arranged.
    /// </remarks>
    [Fact]
    public async Task TwoCreaturesSwappingPositionsEachCarryTheirOwnEightFacts()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await InsertCreatureAsync(path, identity: 4, listType: 0, slot: 12, which: 1);
            await InsertCreatureAsync(path, identity: 5, listType: 0, slot: 13, which: 2);

            await SettleItemsAsync(path, async session =>
            {
                var first = await session.TakeAsync((await session.GetAsync(Main, 12, default))!, null, default);
                var second = await session.TakeAsync((await session.GetAsync(Main, 13, default))!, null, default);
                await session.PutAsync(Main, 13, first, default);
                await session.PutAsync(Main, 12, second, default);
            });

            // The identities have traded positions...
            Assert.Equal(5L, await IdentityAtAsync(path, Main, 12));
            Assert.Equal(4L, await IdentityAtAsync(path, Main, 13));

            // ...and so have the facts, because they are the identity's and not the position's.
            var pets = await CreaturesAsync(side);
            Assert.Equal([4L, 5L], pets.Select(value => value.CreatureKey));
            var first = pets.Single(value => value.CreatureKey == 4);
            var second = pets.Single(value => value.CreatureKey == 5);
            Assert.Equal(Facts(1).ProgressValue, first.ProgressValue);
            Assert.Equal(Facts(1).CreatureText, first.CreatureText);
            Assert.Equal(Facts(1).TailFlag, first.TailFlag);
            Assert.Equal(Facts(2).ProgressValue, second.ProgressValue);
            Assert.Equal(Facts(2).CreatureText, second.CreatureText);
            Assert.Equal(Facts(2).TailFlag, second.TailFlag);
        });
    }

    /// <summary>
    /// The condition written through the store lands on the creature named and on nothing else.
    /// </summary>
    /// <remarks>
    /// The one use case that writes here revives the worn creature, and it names the creature by
    /// its identity. Two are seeded in the same condition, so a statement that had lost its key
    /// would revive both.
    /// </remarks>
    [Fact]
    public async Task TheConditionWriteReachesTheCreatureNamedAndNoOtherRow()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await InsertCreatureAsync(
                path, identity: 4, listType: 3, slot: 24, which: 1, condition: 0);
            await InsertCreatureAsync(
                path, identity: 5, listType: 0, slot: 13, which: 2, condition: 0);

            await SettleCreaturesAsync(
                path, session => session.Creatures.Write(
                    Place, new StoredCreature(4, 1, OnlyWhenAtMostZero: true)));

            var pets = await CreaturesAsync(side);
            Assert.Equal([4L, 5L], pets.Select(value => value.CreatureKey));
            Assert.Equal(1, pets.Single(value => value.CreatureKey == 4).Field04);
            Assert.Equal(0, pets.Single(value => value.CreatureKey == 5).Field04);
        });
    }

    /// <summary>
    /// A condition written against a row that is not a creature is refused rather than making one.
    /// </summary>
    /// <remarks>
    /// Whether a row is a creature is read off the very column this writes, so a statement that
    /// reached a row which is not one would not be a write that missed -- it would be a row that
    /// starts answering as a creature to every read that asks.
    /// </remarks>
    [Fact]
    public async Task AConditionWrittenAgainstSomethingThatIsNotACreatureIsRefused()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await InsertOrdinaryItemAsync(path, identity: 6, slot: 20);

            await Assert.ThrowsAsync<InvalidDataException>(() => SettleCreaturesAsync(
                path,
                session => session.Creatures.Write(Place, new StoredCreature(6, 1))));

            Assert.Empty(await CreaturesAsync(side));
        });
    }

    /// <summary>The eight facts of the nth creature, every one distinct and none defaulted.</summary>
    private static CreatureFacts Facts(int which) => new(
        Field04: 10 + which,
        ModeFlag: 1,
        ProgressValue: 400 + which,
        Mode1Field0A: 20 + which,
        Mode1Field0B: 30 + which,
        FieldAfterValue: 40 + which,
        CreatureText: [.. Enumerable.Range(0, 6).Select(index => (byte)(index + which))],
        TailFlag: 50 + which);

    private sealed record CreatureFacts(
        int Field04,
        int ModeFlag,
        int ProgressValue,
        int Mode1Field0A,
        int Mode1Field0B,
        int FieldAfterValue,
        byte[] CreatureText,
        int TailFlag);

    /// <summary>The creature list this character is served, in the order it is served in.</summary>
    private static async Task<IReadOnlyList<CharacterPetStorageRow>> CreaturesAsync(
        CharacterSide side)
    {
        var loaded = await side.EntryState(new GoldLimitRuleService()).LoadAsync(
            new AccountId(Account), Owner, CancellationToken.None);
        return loaded.Features.Pets;
    }

    /// <summary>Which world row is standing in that position, or nothing when none is.</summary>
    private static async Task<long?> IdentityAtAsync(
        string path, ContainerId containerId, int slot)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT core_uid FROM item_slots "
            + "WHERE owner_scope='character' AND owner_id=@cid "
            + "AND list_type=@list AND slot_index=@slot;";
        command.Parameters.AddWithValue("@cid", Character);
        command.Parameters.AddWithValue("@list", ListTypeOf(containerId));
        command.Parameters.AddWithValue("@slot", slot);
        return await command.ExecuteScalarAsync() is long found ? found : null;
    }

    private static int ListTypeOf(ContainerId containerId)
    {
        Assert.True(containerId.TryGetCharacterOwner(out _, out var containerType));
        return (int)containerType;
    }

    /// <summary>Historical creature core encoded by the SQL seed.</summary>
    private static byte[] CoreBytes(int which)
    {
        var core = new byte[82];
        core[0] = CreatureKind;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1), 63000 + which);
        return core;
    }

    private static async Task SettleItemsAsync(string path, Func<IItemDomainStoreSession, ValueTask> work)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = TestRequired.TransactionScope();
            await using var transaction = scope.Begin();
            await work(new SqliteItemDomainStore(path, TestRequired.NoContentsEvents).SessionIn(scope.Current!));
            await transaction.CommitAsync(CancellationToken.None);
        });
    }

    private static async Task SettleCreaturesAsync(string path, Action<ICharacterDomainStoreSession> work)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = TestRequired.TransactionScope();
            await using var transaction = scope.Begin();
            work(new SqliteCharacterDomainStore(path).SessionIn(scope.Current!));
            await transaction.CommitAsync(CancellationToken.None);
        });
    }

    private static async Task InsertCreatureAsync(
        string path, int identity, int listType, int slot, int which, int? condition = null)
    {
        var facts = Facts(which);
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO items(item_uid,item_core,
                creature_field04,creature_mode_flag,creature_progress_value,
                creature_mode1_field0a,creature_mode1_field0b,creature_field_after_value,
                creature_text,creature_tail_flag)
            VALUES(@uid,@core,@field04,@modeFlag,@progress,
                @mode1a,@mode1b,@afterValue,@text,@tailFlag);
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
            VALUES('character',@cid,@list,@slot,'world',@uid,@uid,1);
            """;
        command.Parameters.AddWithValue("@uid", identity);
        command.Parameters.AddWithValue("@cid", Character);
        command.Parameters.AddWithValue("@list", listType);
        command.Parameters.AddWithValue("@slot", slot);
        command.Parameters.AddWithValue("@core", CoreBytes(which));
        command.Parameters.AddWithValue("@field04", condition ?? facts.Field04);
        command.Parameters.AddWithValue("@modeFlag", facts.ModeFlag);
        command.Parameters.AddWithValue("@progress", facts.ProgressValue);
        command.Parameters.AddWithValue("@mode1a", facts.Mode1Field0A);
        command.Parameters.AddWithValue("@mode1b", facts.Mode1Field0B);
        command.Parameters.AddWithValue("@afterValue", facts.FieldAfterValue);
        command.Parameters.AddWithValue("@text", facts.CreatureText);
        command.Parameters.AddWithValue("@tailFlag", facts.TailFlag);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A row of the same table that is not a creature: none of the eight columns set.</summary>
    private static async Task InsertOrdinaryItemAsync(string path, int identity, int slot)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO items(item_uid,item_core) VALUES(@uid,@core);
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
            VALUES('character',@cid,0,@slot,'world',@uid,@uid,1);
            """;
        command.Parameters.AddWithValue("@uid", identity);
        command.Parameters.AddWithValue("@cid", Character);
        command.Parameters.AddWithValue("@slot", slot);
        command.Parameters.AddWithValue("@core", new byte[82]);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WithASeededCharacterAsync(Func<string, CharacterSide, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var path = Path.Combine(Path.GetTempPath(), $"dfo-creatureid-{Guid.NewGuid():N}.db");
        try
        {
            var side = new CharacterSide(path);
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    INSERT INTO accounts(account_id,m_id) VALUES({Account},'pin');
                    INSERT INTO characters(character_id,account_id,name,job,level)
                    VALUES({Character},{Account},'pin',3,60);
                    INSERT INTO character_stats(character_id) VALUES({Character});
                    INSERT INTO character_condition(character_id,pc_room_state)
                    VALUES({Character},0);
                    INSERT INTO character_pvp(character_id) VALUES({Character});
                    INSERT INTO character_settings(character_id) VALUES({Character});
                    INSERT INTO character_expert_job(character_id) VALUES({Character});
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await body(path, side);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }
        }
    }

}
