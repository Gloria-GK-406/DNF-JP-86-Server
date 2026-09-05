using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// The two tables a thing and its place are kept in, read and written.
/// </summary>
/// <remarks>
/// These hold the two tables to their shape directly, beside the tests that reach them through the
/// entry load and through settlement. A world row says what a thing is and says nothing at all
/// about where it is: the four position columns it used to carry are gone, and a thing's place is
/// a row of the other table naming it.
/// </remarks>
public sealed class WorldAndSlotTableTests
{
    private const string Character = "character";

    /// <summary>A row a slot-only test names without putting anything in the world.</summary>
    /// <remarks>
    /// Every position names the row its bytes are on, and the tests below that are about the slot
    /// table alone have no such row to name. Nothing in the database enforces that it exists, and
    /// these are not the tests that would show it if something did.
    /// </remarks>
    private const int SomeRow = 900;

    /// <summary>A sword: no stack, no avatar facts, no creature facts.</summary>
    private static InventoryItemCoreState Sword(int itemId = 3003) =>
        StackableItemCore.Create(1, itemId, 1) with { EnchantUpgradeCount = 7 };

    /// <summary>A pile of something interchangeable, as the container makes one.</summary>
    private static InventoryItemCoreState Potion(int count, int itemId = 8474) =>
        StackableItemCore.Create(2, itemId, count);

    private static CreatureDetailRow Pet() =>
        new(4, 1, 260, 11, 12, 90, [0x41, 0x42, 0x43], 3);

    private static AvatarDetailRow Avatar() =>
        new(1730000000, 5005, new byte[30], 7, 9, 1740000000);

    // -- the world side -------------------------------------------------------------------------

    /// <summary>A thing put into the world is found again by the identity it was given.</summary>
    [Fact]
    public async Task A_thing_in_the_world_is_found_by_its_identity()
    {
        await using var world = await Fixture.CreateAsync();
        var core = Sword();

        var identity = await world.InUseCaseAsync(
            token => world.Items.InsertAsync(core, null, null, token));
        var row = await world.Items.FindAsync(identity, CancellationToken.None);

        Assert.NotNull(row);
        Assert.Equal(identity, row.ItemUid);
        Assert.Equal(core, row.Core);
        Assert.Null(row.Avatar);
        Assert.Null(row.Creature);
    }

    /// <summary>
    /// A creature carries its eight facts on its own row, and gets them back unchanged.
    /// </summary>
    /// <remarks>
    /// They are the eight <c>character_creatures</c> keeps beside its keys. The keys are what does
    /// not come across: the row is the creature, so there is no second row to keep in step with it
    /// and no separate identity for the two to disagree about.
    /// </remarks>
    [Fact]
    public async Task A_creature_carries_its_eight_facts_on_its_own_row()
    {
        await using var world = await Fixture.CreateAsync();
        var creature = Pet();

        var identity = await world.InUseCaseAsync(
            token => world.Items.InsertAsync(Sword(), null, creature, token));
        var row = await world.Items.FindAsync(identity, CancellationToken.None);

        Assert.NotNull(row?.Creature);
        Assert.Equal(creature.Field04, row.Creature.Field04);
        Assert.Equal(creature.ModeFlag, row.Creature.ModeFlag);
        Assert.Equal(creature.ProgressValue, row.Creature.ProgressValue);
        Assert.Equal(creature.Mode1Field0A, row.Creature.Mode1Field0A);
        Assert.Equal(creature.Mode1Field0B, row.Creature.Mode1Field0B);
        Assert.Equal(creature.FieldAfterValue, row.Creature.FieldAfterValue);
        Assert.Equal(creature.CreatureText, row.Creature.CreatureText);
        Assert.Equal(creature.TailFlag, row.Creature.TailFlag);
        Assert.Null(row.Avatar);
    }

    /// <summary>An avatar's facts are still the row's own columns, beside the creature's.</summary>
    [Fact]
    public async Task An_avatar_carries_its_facts_on_the_same_row()
    {
        await using var world = await Fixture.CreateAsync();
        var avatar = Avatar();

        var identity = await world.InUseCaseAsync(
            token => world.Items.InsertAsync(
                Sword() with { ItemKind = 8, Value = 90210 }, avatar, null, token));
        var row = await world.Items.FindAsync(identity, CancellationToken.None);

        Assert.NotNull(row?.Avatar);
        Assert.Equal(avatar.ExpireDate, row.Avatar.ExpireDate);
        Assert.Equal(avatar.ClearAvatarId, row.Avatar.ClearAvatarId);
        Assert.Equal(avatar.Color1, row.Avatar.Color1);
        Assert.Equal(avatar.Color2, row.Avatar.Color2);
        Assert.Equal(avatar.DeleteDate, row.Avatar.DeleteDate);
        Assert.Null(row.Creature);
    }

    /// <summary>What a thing carries can change; which thing it is cannot.</summary>
    [Fact]
    public async Task What_a_thing_carries_changes_while_its_identity_does_not()
    {
        await using var world = await Fixture.CreateAsync();
        var identity = await world.InUseCaseAsync(
            token => world.Items.InsertAsync(Sword(), null, Pet(), token));

        var grown = Pet() with { ProgressValue = 999 };
        var touched = await world.InUseCaseAsync(
            token => world.Items.UpdateAsync(
                identity, Sword() with { EnchantUpgradeCount = 12 }, null, grown, token));
        var row = await world.Items.FindAsync(identity, CancellationToken.None);

        Assert.Equal(1, touched);
        Assert.NotNull(row?.Creature);
        Assert.Equal(identity, row.ItemUid);
        Assert.Equal(12, row.Core.EnchantUpgradeCount);
        Assert.Equal(999, row.Creature.ProgressValue);
    }

    /// <summary>A thing that stops being a creature stops carrying a creature's facts.</summary>
    [Fact]
    public async Task A_thing_that_stops_being_a_creature_drops_what_only_a_creature_carries()
    {
        await using var world = await Fixture.CreateAsync();
        var identity = await world.InUseCaseAsync(
            token => world.Items.InsertAsync(Sword(), null, Pet(), token));

        await world.InUseCaseAsync(
            token => world.Items.UpdateAsync(identity, Sword(), null, null, token));
        var row = await world.Items.FindAsync(identity, CancellationToken.None);

        Assert.NotNull(row);
        Assert.Null(row.Creature);
    }

    /// <summary>A thing taken out of the world is not there any more.</summary>
    [Fact]
    public async Task A_thing_taken_out_of_the_world_is_gone()
    {
        await using var world = await Fixture.CreateAsync();
        var identity = await world.InUseCaseAsync(
            token => world.Items.InsertAsync(Sword(), null, null, token));

        var taken = await world.InUseCaseAsync(
            token => world.Items.DeleteAsync(identity, token));

        Assert.Equal(1, taken);
        Assert.Null(await world.Items.FindAsync(identity, CancellationToken.None));
    }

    // -- the identity the wire can say ----------------------------------------------------------

    /// <summary>The largest identity the wire can say is still handed out.</summary>
    /// <remarks>
    /// The bound is a bound and not an off-by-one: a creature's identity is a signed 32-bit
    /// integer, and <c>int.MaxValue</c> is one of the values it can be.
    /// </remarks>
    [Fact]
    public async Task The_last_identity_the_wire_can_say_is_still_given_out()
    {
        await using var world = await Fixture.CreateAsync();
        await world.InUseCaseAsync(
            token => world.Items.InsertAsync(Sword(), null, null, token));
        await world.SetNextIdentityAfterAsync(int.MaxValue - 1);

        var identity = await world.InUseCaseAsync(
            token => world.Items.InsertAsync(Sword(), null, null, token));

        Assert.Equal(int.MaxValue, identity);
    }

    /// <summary>
    /// A database out of identities the wire can say refuses to make one, rather than making one
    /// nobody can send.
    /// </summary>
    /// <remarks>
    /// This is the guard, and it is on the column that allocates identities because allocating is
    /// the only way one is ever made. A server that could allocate past this point would put a
    /// creature into a frame it cannot be written into, and the failure would arrive at the
    /// encoder -- a long way from the write that caused it, and by then with the row committed.
    /// </remarks>
    [Fact]
    public async Task An_identity_the_wire_could_not_say_is_never_made()
    {
        await using var world = await Fixture.CreateAsync();
        await world.InUseCaseAsync(
            token => world.Items.InsertAsync(Sword(), null, null, token));
        await world.SetNextIdentityAfterAsync(int.MaxValue);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await world.InUseCaseAsync(
                token => world.Items.InsertAsync(Sword(), null, null, token)));

        Assert.Contains("32-bit", refused.Message, StringComparison.Ordinal);
        Assert.Equal(1L, await world.ScalarAsync("SELECT COUNT(*) FROM items"));
    }

    // -- the slot side --------------------------------------------------------------------------

    /// <summary>
    /// A slot says the same eight things whichever of the two kinds of content it holds.
    /// </summary>
    /// <remarks>
    /// The whole point of the table. A slot holding a sword and a slot holding a stack of potions
    /// are one shape: which kind the id names, the id, the row the bytes are on, and how many.
    /// Neither leaves a column empty, and neither has a column the other lacks -- so nothing
    /// downstream has to know which of two shapes it is looking at before it can read a slot.
    /// </remarks>
    [Fact]
    public async Task A_slot_says_the_same_eight_things_whichever_kind_it_holds()
    {
        await using var world = await Fixture.CreateAsync();
        var identity = await world.InUseCaseAsync(
            token => world.Items.InsertAsync(Sword(), null, null, token));
        var pile = await world.InUseCaseAsync(
            token => world.Items.InsertAsync(Potion(42), null, null, token));

        await world.InUseCaseAsync(async token =>
        {
            await world.Slots.InsertAsync(
                new ItemSlotRow(Place(0, 5), ItemSlotKind.World, identity, identity, 1), token);
            await world.Slots.InsertAsync(
                new ItemSlotRow(Place(0, 6), ItemSlotKind.Template, 8474, pile, 42), token);
        });

        var rows = await world.Slots.ReadByOwnerAsync(Character, 70, CancellationToken.None);
        Assert.Equal(2, rows.Count);
        Assert.Equal(
            new ItemSlotRow(Place(0, 5), ItemSlotKind.World, identity, identity, 1), rows[0]);
        Assert.Equal(
            new ItemSlotRow(Place(0, 6), ItemSlotKind.Template, 8474, pile, 42), rows[1]);

        // Read a second time as columns rather than as a record, because the record could not show
        // a column that was left empty for one of the two kinds.
        Assert.Equal(
            [("character", 70L, 0L, 5L, "world", (long)identity, (long)identity, 1L),
             ("character", 70L, 0L, 6L, "template", 8474L, (long)pile, 42L)],
            await world.RawSlotsAsync());
    }

    /// <summary>One position holds one thing, and the second one to arrive is refused.</summary>
    [Fact]
    public async Task A_position_already_taken_is_left_as_it_was()
    {
        await using var world = await Fixture.CreateAsync();
        await world.InUseCaseAsync(
            token => world.Slots.InsertAsync(
                new ItemSlotRow(Place(0, 5), ItemSlotKind.Template, 8474, SomeRow, 3), token));

        var touched = await world.InUseCaseAsync(
            token => world.Slots.InsertAsync(
                new ItemSlotRow(Place(0, 5), ItemSlotKind.Template, 9999, SomeRow, 1), token));

        Assert.Equal(0, touched);
        var held = await world.Slots.FindAsync(Place(0, 5), CancellationToken.None);
        Assert.Equal(new ItemSlotRow(Place(0, 5), ItemSlotKind.Template, 8474, SomeRow, 3), held);
    }

    /// <summary>An emptied position holds nothing.</summary>
    [Fact]
    public async Task An_emptied_position_holds_nothing()
    {
        await using var world = await Fixture.CreateAsync();
        await world.InUseCaseAsync(
            token => world.Slots.InsertAsync(
                new ItemSlotRow(Place(2, 11), ItemSlotKind.Template, 8474, SomeRow, 3), token));

        var emptied = await world.InUseCaseAsync(
            token => world.Slots.ClearAsync(Place(2, 11), token));

        Assert.Equal(1, emptied);
        Assert.Null(await world.Slots.FindAsync(Place(2, 11), CancellationToken.None));
    }

    /// <summary>There is one of a thing, so a slot naming one cannot claim to hold several.</summary>
    [Fact]
    public async Task A_slot_naming_a_thing_in_the_world_holds_exactly_one_of_it()
    {
        await using var world = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<SqliteException>(async () =>
            await world.InUseCaseAsync(
                token => world.Slots.InsertAsync(
                    new ItemSlotRow(Place(0, 5), ItemSlotKind.World, 1, 1, 2), token)));
    }

    /// <summary>A slot holding none of something is an empty slot, not a row saying nothing.</summary>
    [Fact]
    public async Task A_slot_holding_none_of_something_is_refused()
    {
        await using var world = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<SqliteException>(async () =>
            await world.InUseCaseAsync(
                token => world.Slots.InsertAsync(
                    new ItemSlotRow(Place(0, 5), ItemSlotKind.Template, 8474, SomeRow, 0), token)));
    }

    /// <summary>An owner's slots come back by container and then by position.</summary>
    [Fact]
    public async Task An_owners_slots_come_back_by_container_then_position()
    {
        await using var world = await Fixture.CreateAsync();

        await world.InUseCaseAsync(async token =>
        {
            await world.Slots.InsertAsync(
                new ItemSlotRow(Place(2, 4), ItemSlotKind.Template, 3, SomeRow, 1), token);
            await world.Slots.InsertAsync(
                new ItemSlotRow(Place(0, 9), ItemSlotKind.Template, 1, SomeRow, 1), token);
            await world.Slots.InsertAsync(
                new ItemSlotRow(Place(0, 2), ItemSlotKind.Template, 2, SomeRow, 1), token);
        });

        var rows = await world.Slots.ReadByOwnerAsync(Character, 70, CancellationToken.None);

        Assert.Equal([(0, 2), (0, 9), (2, 4)], rows.Select(row =>
            (row.Place.ListType, row.Place.SlotIndex)));
    }

    /// <summary>A thing and the position holding it are two rows, written apart.</summary>
    /// <remarks>
    /// The fact the whole shape exists for. Reading the slot gives an identity, and reading that
    /// identity gives the thing; neither read had to go through the other's table.
    /// </remarks>
    [Fact]
    public async Task A_thing_and_the_position_holding_it_are_two_separate_rows()
    {
        await using var world = await Fixture.CreateAsync();

        var identity = await world.InUseCaseAsync(async token =>
        {
            var made = await world.Items.InsertAsync(Sword(), null, Pet(), token);
            await world.Slots.InsertAsync(
                new ItemSlotRow(Place(1, 24), ItemSlotKind.World, made, made, 1), token);
            return made;
        });

        var slot = await world.Slots.FindAsync(Place(1, 24), CancellationToken.None);
        Assert.NotNull(slot);
        Assert.Equal(ItemSlotKind.World, slot.Kind);

        var thing = await world.Items.FindAsync(slot.Id, CancellationToken.None);
        Assert.NotNull(thing?.Creature);
        Assert.Equal(identity, thing.ItemUid);
    }

    // -- what nothing holds ---------------------------------------------------------------------

    /// <summary>A thing no position holds is still there, and asking finds it.</summary>
    /// <remarks>
    /// The state the world row exists in order to be able to express: the thing outlives every
    /// position it was ever in, so it can be in none. Nothing here sweeps it up -- the point of
    /// this task is that such a thing can be asked for at all.
    /// </remarks>
    [Fact]
    public async Task A_thing_no_position_holds_is_found_by_asking_for_those()
    {
        await using var world = await Fixture.CreateAsync();
        var identity = await world.InUseCaseAsync(
            token => world.Items.InsertAsync(Sword(), null, Pet(), token));

        var unheld = await world.Items.ReadUnreferencedAsync(CancellationToken.None);

        var only = Assert.Single(unheld);
        Assert.Equal(identity, only.ItemUid);
        Assert.NotNull(only.Creature);
        Assert.Equal(260, only.Creature.ProgressValue);
    }

    /// <summary>A thing a position holds is not one of them.</summary>
    [Fact]
    public async Task A_thing_a_position_holds_is_not_one_of_those()
    {
        await using var world = await Fixture.CreateAsync();
        await world.InUseCaseAsync(async token =>
        {
            var made = await world.Items.InsertAsync(Sword(), null, null, token);
            await world.Slots.InsertAsync(
                new ItemSlotRow(Place(0, 5), ItemSlotKind.World, made, made, 1), token);
        });

        Assert.Empty(await world.Items.ReadUnreferencedAsync(CancellationToken.None));
    }

    /// <summary>
    /// A pile's row is not one of them, although the position holding it names a template.
    /// </summary>
    /// <remarks>
    /// <b>The distinction this read has to make.</b> Something interchangeable keeps a row of
    /// <c>items</c> as well -- its core carries values a template id and a count cannot rebuild --
    /// but the position holding it names its template rather than its row, so asking only "does a
    /// slot's id name this row" would answer that every pile in the database is unheld.
    ///
    /// What separates the two is that a position names the row its bytes are on whichever kind it
    /// holds, so a pile's row is named by the position holding the pile and a thing nothing holds
    /// is named by nothing. Both are written here into one database so that the answer has to tell
    /// them apart rather than happening to contain the right one.
    /// </remarks>
    [Fact]
    public async Task A_piles_row_is_not_one_of_those()
    {
        await using var world = await Fixture.CreateAsync();
        var abandoned = await world.InUseCaseAsync(async token =>
        {
            // A pile, written the way settlement writes one: a row of items carrying its core,
            // and a position saying which template it holds, how many, and which row they are on.
            var heap = await world.Items.InsertAsync(Potion(40), null, null, token);
            await world.Slots.InsertAsync(
                new ItemSlotRow(Place(0, 5), ItemSlotKind.Template, 8474, heap, 40), token);

            // A piece of gear a position does hold.
            var worn = await world.Items.InsertAsync(Sword(), null, null, token);
            await world.Slots.InsertAsync(
                new ItemSlotRow(Place(3, 2), ItemSlotKind.World, worn, worn, 1), token);

            // And one nothing holds.
            return await world.Items.InsertAsync(Sword(4004), null, Pet(), token);
        });

        var unheld = await world.Items.ReadUnreferencedAsync(CancellationToken.None);

        Assert.Equal([abandoned], unheld.Select(row => row.ItemUid));
    }

    private static ItemPlace Place(int listType, int slotIndex) =>
        new(Character, 70, listType, slotIndex);

    /// <summary>One database, its two tables, and a use case to write inside.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string path)
        {
            this.path = path;
            this.scope = new DomainTransactionScope();
            var database = new SqliteDatabase(path, this.scope);
            this.Items = new SqliteWorldItemTable(database);
            this.Slots = new SqliteItemSlotTable(database);
        }

        private readonly string path;

        private readonly DomainTransactionScope scope;

        public SqliteWorldItemTable Items { get; }

        public SqliteItemSlotTable Slots { get; }

        public static Task<Fixture> CreateAsync() => Task.FromResult(
            new Fixture(Path.Combine(
                Path.GetTempPath(), $"dfo-world-slot-{Guid.NewGuid():N}.db")));

        /// <summary>Runs one call inside a use case and settles it, as one command would.</summary>
        public async Task<TResult> InUseCaseAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> work)
        {
            await using var lease = this.scope.Begin();
            var result = await work(CancellationToken.None).ConfigureAwait(false);
            await lease.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return result;
        }

        /// <summary>The same, for work that answers with nothing.</summary>
        public async Task InUseCaseAsync(Func<CancellationToken, ValueTask> work)
        {
            await using var lease = this.scope.Begin();
            await work(CancellationToken.None).ConfigureAwait(false);
            await lease.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>Leaves the allocator about to hand out the identity after that one.</summary>
        /// <remarks>
        /// The counter only exists once something has been inserted, which is why every test using
        /// this puts one thing in first. Driving the allocator up to its bound by inserting two
        /// billion rows is not a test anyone can run.
        /// </remarks>
        public async Task SetNextIdentityAfterAsync(long allocated)
        {
            await using var connection = this.Open();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE sqlite_sequence SET seq=@seq WHERE name='items'";
            command.Parameters.AddWithValue("@seq", allocated);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        public async Task<long> ScalarAsync(string statement)
        {
            await using var connection = this.Open();
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            return (long)(await command.ExecuteScalarAsync())!;
        }

        /// <summary>Every slot as its eight columns, so an empty one shows as the null it is.</summary>
        public async Task<List<(string, long, long, long, string, long, long, long)>>
            RawSlotsAsync()
        {
            await using var connection = this.Open();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count "
                + "FROM item_slots ORDER BY list_type,slot_index";
            var rows = new List<(string, long, long, long, string, long, long, long)>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                for (var ordinal = 0; ordinal < 8; ordinal++)
                {
                    Assert.False(
                        await reader.IsDBNullAsync(ordinal),
                        $"A slot left {reader.GetName(ordinal)} empty.");
                }

                rows.Add((
                    reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2),
                    reader.GetInt64(3), reader.GetString(4), reader.GetInt64(5),
                    reader.GetInt64(6), reader.GetInt64(7)));
            }

            return rows;
        }

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = this.path }.ToString());
            connection.Open();
            return connection;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(this.path);
            }
            catch (IOException)
            {
                // A temporary file the operating system will clear. Failing here would report a
                // passing assertion as a broken one.
            }

            return ValueTask.CompletedTask;
        }
    }
}
