using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Quests;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// What a character's combo pages answer, and what asking the book to change one settles.
/// </summary>
/// <remarks>
/// The store underneath is the real one, over a real database, because what is being checked is
/// that the book answers what is stored. A stand-in store would answer with what the test handed
/// it and prove nothing about the rows.
///
/// The two pages are given bodies that cannot stand in for one another, so a book that answered
/// the wrong page -- or dropped one -- moves an assertion rather than passing by coincidence.
/// </remarks>
public sealed class DarkKnightComboBookTests
{
    private static readonly AccountId Account = new(6);

    private static readonly CharacterId Character = new(60);

    /// <summary>Page zero's body. Shares no byte with page one's.</summary>
    private static readonly byte[] FirstBody = [0x0A, 0x0B, 0x0C];

    /// <summary>Page one's body. A different length as well as different bytes.</summary>
    private static readonly byte[] SecondBody = [0x1D, 0x1E];

    [Fact]
    public async Task WhatIsAnsweredIsWhatTheRowsHold()
    {
        await using var fixture = await Fixture.CreateAsync();
        // Written in the other order on purpose: the order of the answer is the page number's,
        // not the order the rows happened to be inserted in.
        await fixture.SeedPagesAsync((1, SecondBody), (0, FirstBody));

        var pages = await fixture.Book().PagesAsync(CancellationToken.None);

        Assert.Equal(2, pages.Count);
        Assert.Equal<byte>(0, pages[0].PageIndex);
        Assert.Equal(FirstBody, pages[0].EncodedBody);
        Assert.Equal<byte>(1, pages[1].PageIndex);
        Assert.Equal(SecondBody, pages[1].EncodedBody);
    }

    [Fact]
    public async Task APageIsAnsweredByItsNumber()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedPagesAsync((0, FirstBody), (1, SecondBody));
        var book = fixture.Book();

        var first = await book.FindPageAsync(0, CancellationToken.None);
        var second = await book.FindPageAsync(1, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal<byte>(0, first.PageIndex);
        Assert.Equal(FirstBody, first.EncodedBody);
        Assert.NotNull(second);
        Assert.Equal<byte>(1, second.PageIndex);
        Assert.Equal(SecondBody, second.EncodedBody);
    }

    /// <remarks>
    /// Nothing rather than an empty page: a page never stored and a page whose body is empty are
    /// different states of the rows.
    /// </remarks>
    [Fact]
    public async Task APageTheCharacterDoesNotHaveIsAnsweredWithNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedPagesAsync((0, FirstBody));

        Assert.Null(await fixture.Book().FindPageAsync(1, CancellationToken.None));
    }

    /// <remarks>
    /// Every character has a book, including every character who is not a Dark Knight. What makes
    /// it empty is that there are no rows, not what job the character is -- the book never asks.
    /// </remarks>
    [Fact]
    public async Task ACharacterWithNoRowsHasAnEmptyBook()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Empty(await fixture.Book().PagesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ABookKnowsWhoseItIs()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Equal(Character, fixture.Book().CharacterId);
    }

    [Fact]
    public async Task TwoBooksAskedForTheSameCharacterAreTheSameBook()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Books.BookOf(Account, Character)
            .SetPageAsync(new DarkKnightComboEntryState(0, FirstBody), CancellationToken.None);

        var pages = await fixture.Books.BookOf(Account, Character)
            .PagesAsync(CancellationToken.None);
        Assert.Equal(FirstBody, Assert.Single(pages).EncodedBody);
    }

    /// <remarks>
    /// The write is staged until the use case settles, and it is the store that settles it: before
    /// the transaction commits the row is not there, and the same page read back afterwards is the
    /// one that was written.
    /// </remarks>
    [Fact]
    public async Task APageWrittenThroughTheBookIsSettledByTheUseCase()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Book().SetPageAsync(
            new DarkKnightComboEntryState(1, SecondBody), CancellationToken.None);
        Assert.Empty(await fixture.StoredPagesAsync());

        await fixture.SettleAsync();

        var stored = Assert.Single(await fixture.StoredPagesAsync());
        Assert.Equal(1, stored.PageIndex);
        Assert.Equal(SecondBody, stored.Body);
    }

    /// <remarks>
    /// Its own use case sees the write before anyone else can, which is what reading through the
    /// store rather than through the table on its own gets. A book that cached would answer the
    /// same way here and the wrong way in the test below.
    /// </remarks>
    [Fact]
    public async Task ABookSeesItsOwnUseCasesUnsettledWrite()
    {
        await using var fixture = await Fixture.CreateAsync();
        var book = fixture.Book();

        await book.SetPageAsync(
            new DarkKnightComboEntryState(0, FirstBody), CancellationToken.None);

        var page = await book.FindPageAsync(0, CancellationToken.None);
        Assert.NotNull(page);
        Assert.Equal(FirstBody, page.EncodedBody);
        Assert.Empty(await fixture.StoredPagesAsync());
    }

    [Fact]
    public async Task APageWrittenAndThenTakenBackIsNotThere()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Book().SetPageAsync(
            new DarkKnightComboEntryState(0, FirstBody), CancellationToken.None);
        await fixture.RollBackAsync();

        Assert.Empty(await fixture.Book().PagesAsync(CancellationToken.None));
        Assert.Empty(await fixture.StoredPagesAsync());
    }

    [Fact]
    public async Task ClearingAPageLeavesTheCharacterWithoutIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedPagesAsync((0, FirstBody), (1, SecondBody));

        await fixture.Book().ClearPageAsync(0, CancellationToken.None);
        await fixture.SettleAsync();

        var left = Assert.Single(await fixture.StoredPagesAsync());
        Assert.Equal(1, left.PageIndex);
        Assert.Equal(SecondBody, left.Body);
        Assert.Null(await fixture.Book().FindPageAsync(0, CancellationToken.None));
    }

    /// <remarks>
    /// The entry load refuses a page outside the two it will carry, so a store that let one be
    /// written would not be storing a wrong answer but an unusable character.
    /// </remarks>
    [Fact]
    public async Task APageTheEntryLoadWouldNotCarryIsRefused()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await fixture.Book().SetPageAsync(
                new DarkKnightComboEntryState(2, FirstBody), CancellationToken.None));
        Assert.Empty(await fixture.StoredPagesAsync());
    }

    /// <remarks>
    /// The same refusal on the way out. A row the entry load will not carry is one this book will
    /// not hand back either: answering with it would describe a character who cannot enter.
    /// </remarks>
    [Fact]
    public async Task ARowTheEntryLoadWouldNotCarryIsRefusedWhenRead()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedPagesAsync((2, FirstBody));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.Book().PagesAsync(CancellationToken.None));
    }

    /// <remarks>
    /// A body past what the read of these rows allows is the same kind of unusable page as a page
    /// number the entry load will not carry, and is turned away the same way. The read bound is
    /// <see cref="EntryBlob.Combo"/>, applied by the gateway this store reads its rows through; a
    /// write that took a longer body would settle a row that every later read of this character's
    /// pages then threw on.
    ///
    /// Named by the read's bound rather than by a number, because what is being pinned is that the
    /// two agree, not what they happen to agree on.
    /// </remarks>
    [Fact]
    public async Task ABodyTheReadWouldRefuseIsRefusedWhenWritten()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await fixture.Book().SetPageAsync(
                new DarkKnightComboEntryState(0, new byte[EntryBlob.Combo + 1]),
                CancellationToken.None));
        Assert.Empty(await fixture.StoredPagesAsync());
    }

    /// <remarks>
    /// And no tighter than the read either. A body of exactly the bound is one the read hands
    /// back, so refusing it on the way in would be the same asymmetry the other way round.
    /// </remarks>
    [Fact]
    public async Task ABodyExactlyAtTheReadsBoundIsSettledAndReadBack()
    {
        await using var fixture = await Fixture.CreateAsync();
        var body = new byte[EntryBlob.Combo];
        // Not all zeroes, and marked at the far end, so a body truncated anywhere fails.
        body[0] = 0x5A;
        body[^1] = 0xA5;

        await fixture.Book().SetPageAsync(
            new DarkKnightComboEntryState(0, body), CancellationToken.None);
        await fixture.SettleAsync();

        var page = await fixture.Book().FindPageAsync(0, CancellationToken.None);
        Assert.NotNull(page);
        Assert.Equal(body, page.EncodedBody);
    }

    /// <summary>
    /// The combo store adds nothing to what a use case settles.
    /// </summary>
    /// <remarks>
    /// A transaction keys enlistment on identity, and this store answers the identity every other
    /// store over the same database answers: the connection string. So it joins the participant
    /// that is already there instead of becoming one of its own, and the list the transaction
    /// settles in order is the same list it was before this store existed. That is the whole of
    /// why adding it cannot move when anything else is settled.
    ///
    /// Checked against the three stores that were already there rather than asserted about this
    /// one alone, because "adds no participant" is a statement about the set.
    /// </remarks>
    [Fact]
    public async Task TheComboStoreJoinsTheParticipantTheOtherStoresAlreadyJoined()
    {
        await using var fixture = await Fixture.CreateAsync();
        var transaction = new CountingTransaction();

        _ = new SqliteCharacterDomainStore(fixture.Path).SessionIn(transaction);
        _ = new SqliteItemDomainStore(fixture.Path).SessionIn(transaction);
        _ = new SqliteQuestDomainStore(fixture.Path).SessionIn(transaction);
        _ = fixture.Store.SessionIn(transaction);

        Assert.Single(transaction.Participants, x => x.CommitCanFail);
        Assert.Single(transaction.Participants.OfType<Dfo.Server.Abstractions.Inventory.ItemLifecycleScope>());
    }

    /// <summary>One database, wired the way the server wires this book.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string path)
        {
            this.Path = path;
            var database = new SqliteDatabase(path, new DomainTransactionScope());
            this.Transactions = new OneUseCaseAtATime();
            this.Store = new SqliteDarkKnightComboDomainStore(
                path, new SqliteCharacterComboPageTable(database));
            this.Books = new CharacterDarkKnightComboDomainService(
                this.Store, new AmbientStoreAccess(this.Transactions));
        }

        public string Path { get; }

        public SqliteDarkKnightComboDomainStore Store { get; }

        public OneUseCaseAtATime Transactions { get; }

        public ICharacterDarkKnightComboDomainService Books { get; }

        public IDarkKnightComboBook Book() => this.Books.BookOf(Account, Character);

        public static async Task<Fixture> CreateAsync()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-combopages-{Guid.NewGuid():N}.db");
            // The database owns the schema; the store only writes into it.
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            var fixture = new Fixture(path);
            await using var connection = Open(path);
            await using var character = connection.CreateCommand();
            character.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'combos')";
            await character.ExecuteNonQueryAsync();
            return fixture;
        }

        /// <summary>Settles the open use case and opens the next one.</summary>
        public async Task SettleAsync()
        {
            await this.Transactions.Open.CommitAsync(CancellationToken.None);
            this.Transactions.Reopen();
        }

        /// <summary>Takes the open use case back and opens the next one.</summary>
        public async Task RollBackAsync()
        {
            await this.Transactions.Open.RollbackAsync();
            this.Transactions.Reopen();
        }

        /// <summary>Puts rows in place directly, which is how a page gets there today.</summary>
        public async Task SeedPagesAsync(params (int PageIndex, byte[] Body)[] rows)
        {
            await using var connection = Open(this.Path);
            foreach (var (pageIndex, body) in rows)
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO character_dark_knight_combo_skill_pages(character_id,page_index,body) "
                    + "VALUES(60,@page,@body)";
                command.Parameters.AddWithValue("@page", pageIndex);
                command.Parameters.AddWithValue("@body", body);
                await command.ExecuteNonQueryAsync();
            }
        }

        /// <summary>What the table itself holds, read outside anything this test wired.</summary>
        public async Task<IReadOnlyList<(int PageIndex, byte[] Body)>> StoredPagesAsync()
        {
            await using var connection = Open(this.Path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT page_index, body FROM character_dark_knight_combo_skill_pages "
                + "WHERE character_id=60 ORDER BY page_index";
            var rows = new List<(int, byte[])>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetInt32(0), (byte[])reader.GetValue(1)));
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
                File.Delete(this.Path);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Stands in for the pipeline's ambient transaction, one use case at a time.</summary>
    private sealed class OneUseCaseAtATime : IDomainTransactionAccessor
    {
        public DomainTransaction Open { get; private set; } = new();

        public IDomainTransaction? Current => this.Open;

        public void Reopen() => this.Open = new DomainTransaction();
    }

    /// <summary>Keeps what joined, so a test can prove one participant rather than two.</summary>
    private sealed class CountingTransaction : IDomainTransaction
    {
        private readonly Dictionary<object, ITransactionParticipant> enlisted = [];

        public IReadOnlyCollection<ITransactionParticipant> Participants => this.enlisted.Values;

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
    }
}
