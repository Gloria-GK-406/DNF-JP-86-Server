using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Quests;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// One use case changing a character, its quests, and the items it owns.
/// </summary>
/// <remarks>
/// This is what the whole boundary was built for, and it is not something any one store can show
/// on its own: each has its own settlement tests, but only together do they answer whether a use
/// case that touches several leaves them agreeing. Before the participant was keyed on the
/// database, this grouping could not even be assembled -- the second store to join was refused.
/// </remarks>
public sealed class CharacterDomainStoreSettlementTests
{
    private static readonly OwnedCharacterId Owned =
        new(new AccountId(6), new CharacterId(60));

    private static readonly ContainerId Main =
        ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Main);

    [Fact]
    public async Task AUseCaseThatEndsWellLeavesBothTheCharacterAndItsItemsChanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var transaction = new DomainTransaction();

        var items = fixture.Items.SessionIn(transaction);
        await items.PutAsync(Main, 66, await items.CreateAsync(ItemSnapshot.FromCore(Potion(3), true), default), default);
        fixture.Quests.SessionIn(transaction).Carried.Write(
            Owned, new StoredActiveQuest(2, 200, 9, ExpectedTriggerValue: 6));
        await transaction.CommitAsync(CancellationToken.None);

        Assert.Equal([(65, 5), (66, 3)], await fixture.ReadItemsAsync());
        Assert.Equal(9, await fixture.ReadQuestTriggerAsync());
    }

    /// <remarks>
    /// The quest write carries what the caller last saw. Giving it a value the row never held is
    /// how a stale entry snapshot would present itself, and it fails while the database is turning
    /// the staged writes into statements -- the moment both kinds of state have work outstanding
    /// and neither is visible yet.
    /// </remarks>
    [Fact]
    public async Task AUseCaseThatFailsLeavesNeitherTheCharacterNorItsItemsChanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var transaction = new DomainTransaction();

        var items = fixture.Items.SessionIn(transaction);
        await items.PutAsync(Main, 66, await items.CreateAsync(ItemSnapshot.FromCore(Potion(3), true), default), default);
        fixture.Quests.SessionIn(transaction).Carried.Write(
            Owned, new StoredActiveQuest(2, 200, 9, ExpectedTriggerValue: 1234));

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await transaction.CommitAsync(CancellationToken.None));
        Assert.Equal([(65, 5)], await fixture.ReadItemsAsync());
        Assert.Equal(6, await fixture.ReadQuestTriggerAsync());
    }

    [Fact]
    public async Task TheCharacterAndItsItemsTakePartAsOneParticipant()
    {
        await using var fixture = await Fixture.CreateAsync();
        var transaction = new DomainTransaction();

        _ = fixture.Items.SessionIn(transaction);
        _ = fixture.Characters.SessionIn(transaction);
        _ = fixture.Quests.SessionIn(transaction);

        // A second participant whose commit can fail would have been refused outright; that this
        // returns at all is the whole point of keying enlistment on the database.
        Assert.True(fixture.Characters.IsDurable && fixture.Items.IsDurable
                    && fixture.Quests.IsDurable);
        Assert.Equal(fixture.Items.StoreIdentity, fixture.Characters.StoreIdentity);
        Assert.Equal(fixture.Items.StoreIdentity, fixture.Quests.StoreIdentity);
    }

    /// <remarks>
    /// The account is half of the address, so a character named under an account that does not own
    /// it is not an address this store can place, and the write fails rather than landing on rows
    /// nobody can reach.
    /// </remarks>
    [Fact]
    public async Task ACharacterNamedUnderTheWrongAccountIsRefused()
    {
        await using var fixture = await Fixture.CreateAsync();
        var transaction = new DomainTransaction();

        fixture.Characters.SessionIn(transaction).WritePosition(
            new OwnedCharacterId(new AccountId(999), new CharacterId(60)),
            new CharacterPositionState(1, 2, 3, 4, 5));

        var refusal = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await transaction.CommitAsync(CancellationToken.None));

        // The words matter: a position refused for its owner has always said so in its own terms,
        // and the shared ownership check must not answer in place of it.
        Assert.Equal("Entry position owner is unavailable.", refusal.Message);
    }

    /// <remarks>
    /// A write with nothing to compare against puts the slot in that state whatever it held, and
    /// creates it when it held nothing. It is how a quest is taken on -- there is nothing to
    /// compare against a row that does not exist yet -- and this is where that half of the
    /// contract is checked, apart from any caller.
    /// </remarks>
    [Fact]
    public async Task AQuestWrittenWithNothingToCompareAgainstLandsRegardless()
    {
        await using var fixture = await Fixture.CreateAsync();
        var transaction = new DomainTransaction();

        var rows = fixture.Quests.SessionIn(transaction);
        rows.Carried.Write(Owned, new StoredActiveQuest(2, 200, 11));
        rows.Carried.Write(Owned, new StoredActiveQuest(3, 300, 12));
        await transaction.CommitAsync(CancellationToken.None);

        Assert.Equal(11, await fixture.ReadQuestTriggerAsync());
        Assert.Equal(12, await fixture.ReadQuestTriggerAsync(slot: 3));
    }

    /// <remarks>
    /// Reading back what this session has staged but not yet settled is what lets a use case decide
    /// against what it has already decided rather than against what the database still says.
    /// </remarks>
    [Fact]
    public async Task ASessionSeesItsOwnUnsettledWrites()
    {
        await using var fixture = await Fixture.CreateAsync();
        var rows = fixture.Quests.SessionIn(new DomainTransaction());

        rows.Carried.Write(Owned, new StoredActiveQuest(2, 200, 42, ExpectedTriggerValue: 6));
        var seen = await rows.Carried.ReadAsync(Owned, 2, CancellationToken.None);

        Assert.Equal(42, seen!.TriggerValue);
        Assert.Equal(6, await fixture.ReadQuestTriggerAsync());
    }

    /// <remarks>
    /// Finishing a quest and asking to be told about one are the two kinds of quest state that had
    /// nowhere to be written before: the tables existed and only ever answered reads. A round trip
    /// through the store is what says they are writable at all.
    /// </remarks>
    [Fact]
    public async Task WhatWasFinishedAndWhatIsBeingWatchedComeBackOut()
    {
        await using var fixture = await Fixture.CreateAsync();
        var transaction = new DomainTransaction();

        var rows = fixture.Quests.SessionIn(transaction);
        rows.Cleared.Write(Owned, new StoredClearedQuest(1016, 1));
        rows.Tracked.Write(Owned, new StoredTrackedQuest(0, 1016));
        rows.Tracked.Write(Owned, new StoredTrackedQuest(1, 101));
        await transaction.CommitAsync(CancellationToken.None);

        var read = fixture.Quests.SessionIn(new DomainTransaction());
        Assert.Equal(
            [new StoredClearedQuest(1016, 1)],
            await read.Cleared.ReadAllAsync(Owned, CancellationToken.None));
        Assert.Equal(
            [new StoredTrackedQuest(0, 1016), new StoredTrackedQuest(1, 101)],
            await read.Tracked.ReadAllAsync(Owned, CancellationToken.None));
    }

    /// <remarks>
    /// The other half of those two sections. Emptying a place is part of what a store session
    /// promises, and here it is the only thing that runs their delete statements: no verb of the
    /// quest log ever unfinishes a quest, and a shortened tracking selection only reaches the
    /// tracking one. A statement nothing executes is a statement nothing would notice was wrong.
    /// </remarks>
    [Fact]
    public async Task WhatWasFinishedAndWhatIsBeingWatchedCanBeTakenAwayAgain()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seeding = new DomainTransaction();
        var seed = fixture.Quests.SessionIn(seeding);
        seed.Cleared.Write(Owned, new StoredClearedQuest(1016, 1));
        seed.Tracked.Write(Owned, new StoredTrackedQuest(0, 1016));
        await seeding.CommitAsync(CancellationToken.None);

        var transaction = new DomainTransaction();
        var rows = fixture.Quests.SessionIn(transaction);
        rows.Cleared.Clear(Owned, 1016);
        rows.Tracked.Clear(Owned, 0);
        await transaction.CommitAsync(CancellationToken.None);

        var read = fixture.Quests.SessionIn(new DomainTransaction());
        Assert.Empty(await read.Cleared.ReadAllAsync(Owned, CancellationToken.None));
        Assert.Empty(await read.Tracked.ReadAllAsync(Owned, CancellationToken.None));
    }

    /// <remarks>
    /// The quest number is the flag array's index, and the entry projection refuses a row past its
    /// end. Refused while staging rather than while flushing, because by then the other kinds of
    /// state have written their statements and a refusal raised there would take them down too.
    /// </remarks>
    [Fact]
    public async Task AFinishedQuestPastTheFlagArrayIsRefusedWhereItIsAskedFor()
    {
        await using var fixture = await Fixture.CreateAsync();
        var rows = fixture.Quests.SessionIn(new DomainTransaction());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => rows.Cleared.Write(Owned, new StoredClearedQuest(30_000, 1)));
    }

    private const int PotionId = 1000;

    private static InventoryItemCoreState Potion(int count) =>
        new(6, PotionId, count, 0, 0, 0, 0, 0, 0, 0, -1,
            new ChronicleOptionState(0, 0, 0, 0, 0), new ChronicleOptionState(0, 0, 0, 0, 0),
            0, 0, 0, 0, 0,
            new RandomOptionState(0, 0, 0), new RandomOptionState(0, 0, 0),
            new RandomOptionState(0, 0, 0), 0, byte.MaxValue, 0, new RandomOptionState(0, 0, 0),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string path)
        {
            this.Path = path;
            this.Items = new SqliteItemDomainStore(path);
            this.Characters = new SqliteCharacterDomainStore(path);
            this.Quests = new SqliteQuestDomainStore(path);
        }

        public string Path { get; }

        public SqliteItemDomainStore Items { get; }

        public SqliteCharacterDomainStore Characters { get; }

        public SqliteQuestDomainStore Quests { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-charstore-{Guid.NewGuid():N}.db");
            // The database owns the schema; the stores only write into it.
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            var fixture = new Fixture(path);
            await using var connection = Open(path);
            await using (var character = connection.CreateCommand())
            {
                character.CommandText =
                    "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'both')";
                await character.ExecuteNonQueryAsync();
            }

            await using (var quest = connection.CreateCommand())
            {
                quest.CommandText = "INSERT INTO character_active_quests VALUES(60,2,200,6)";
                await quest.ExecuteNonQueryAsync();
            }

            await using var item = connection.CreateCommand();
            item.CommandText =
                // A thing and the position holding it. A seeded row is two rows now: the world
                // row carries no position of its own for a fixture to fill in.
                "INSERT INTO items(item_core) VALUES(@core);"
                + "INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) "
                + "VALUES('character',60,0,65,'world',last_insert_rowid(),"
                + "last_insert_rowid(),1)";
            item.Parameters.AddWithValue(
                "@core", InventoryItemCore.Encode(Potion(5)));
            await item.ExecuteNonQueryAsync();
            return fixture;
        }

        public async Task<List<(int Slot, int Count)>> ReadItemsAsync()
        {
            await using var connection = Open(this.Path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT place.slot_index,held.item_core "
                + "FROM item_slots place JOIN items held ON held.item_uid=place.core_uid "
                + "WHERE place.owner_scope='character' AND place.owner_id=60 "
                + "AND place.list_type=0 ORDER BY place.slot_index";
            var rows = new List<(int, int)>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((
                    reader.GetInt32(0),
                    InventoryItemCore.Decode((byte[])reader.GetValue(1)).Value));
            }

            return rows;
        }

        public async Task<int> ReadQuestTriggerAsync(int slot = 2)
        {
            await using var connection = Open(this.Path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT trigger_value FROM character_active_quests WHERE character_id=60 AND slot=@slot";
            command.Parameters.AddWithValue("@slot", slot);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection(
                SqliteTestPools.ConnectionString(path));
            connection.Open();
            return connection;
        }

        public ValueTask DisposeAsync()
        {
            SqliteTestPools.ClearOwned(this.Path);
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
