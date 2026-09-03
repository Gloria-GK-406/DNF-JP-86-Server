using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// The gold a character is carrying -- the figure on the player's bag.
/// </summary>
/// <remarks>
/// Against real SQLite rather than a stand-in, because the whole of what is under test is what the
/// statements do. Both directions are one statement each, and that is the point: the condition
/// that says a character can pay and the subtraction that makes it pay are the same statement, and
/// the ceiling is a parameter of the addition rather than a comparison made afterwards. A fake
/// table would be asserting its own arithmetic and would go on passing if either statement lost
/// its guard.
/// </remarks>
public sealed class SqliteCharacterCarriedGoldTableTests
{
    private static readonly CharacterId Character = new(11);

    /// <summary>What the wire field can carry, which is what the ceiling is when nothing caps it.</summary>
    private const int NoCap = int.MaxValue;

    /// <summary>The level the fixture's character stands at.</summary>
    private const int CharacterLevel = 86;

    private static async ValueTask<int> Carried(SqliteCharacterCarriedGoldTable table) =>
        (await table.FindAsync(Character, CancellationToken.None))?.Carried ?? -1;

    /// <summary>An existing character carries zero rather than nothing.</summary>
    /// <remarks>
    /// The row is opened when the character is created, so "no row" is not a state this has to
    /// mean anything by. A character carrying nothing carries zero, and that is a figure rather
    /// than an absence -- which is what makes a balance of zero tell an auditor something.
    /// </remarks>
    [Fact]
    public async Task A_new_character_carries_zero_and_has_a_row_to_carry_it_in()
    {
        var (table, path) = Open();
        try
        {
            var found = await table.FindAsync(Character, CancellationToken.None);

            Assert.NotNull(found);
            Assert.Equal(0, found.Carried);
            Assert.Equal(CharacterLevel, found.Level);
            Assert.Equal(0, found.StoredCarryLimit);
            Assert.Equal(700, await table.AddAsync(Character, 700, NoCap, CancellationToken.None));
        }
        finally
        {
            Discard(path);
        }
    }

    /// <summary>
    /// Additions answered at the same moment all reach the figure.
    /// </summary>
    /// <remarks>
    /// <b>On real threads, because awaiting is not racing here.</b> This driver's asynchronous
    /// members complete synchronously, so a <c>WhenAll</c> over calls made on this thread runs
    /// them one after another and proves only that addition accumulates -- which no plausible
    /// defect breaks. What has to be shown is that two changes genuinely at once cannot each read
    /// the same figure and each write their own sum, so the calls are put on the thread pool and
    /// released together.
    /// <para>
    /// The addition is one statement precisely so that they cannot, and nothing else in this
    /// server would stop them: the per-character execution gate it declares has no callers, and
    /// its transactions are deferred.
    /// </para>
    /// <para>
    /// A figure that lost a coin to a race is the defect nobody can reconstruct afterwards: the
    /// player only knows they are poorer than they expected.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Additions_made_at_the_same_moment_all_reach_the_figure()
    {
        var (table, path) = Open();
        try
        {
            // Eight, not more. Genuine contention costs real time here -- the store's writes
            // serialise on the file's one write lock -- and the property holds at any count above
            // one, so the case buys its evidence at the smallest count that buys it.
            const int Racers = 8;
            using var released = new Barrier(Racers);
            await Task.WhenAll(Enumerable.Range(0, Racers).Select(_ => Task.Run(async () =>
            {
                released.SignalAndWait();
                await table.AddAsync(Character, 1, NoCap, CancellationToken.None);
            })));

            Assert.Equal(Racers, await Carried(table));
        }
        finally
        {
            Discard(path);
        }
    }

    /// <summary>The figure is held between zero and the ceiling it is given.</summary>
    /// <remarks>
    /// Both ends in one case and on the same character, because apart they prove less: that an
    /// addition stops at the cap is worth something beside the fact that it also cannot be driven
    /// below zero, and a statement that lost either clamp would still pass a test for the other.
    /// </remarks>
    [Fact]
    public async Task The_figure_is_held_between_zero_and_the_ceiling()
    {
        var (table, path) = Open();
        try
        {
            Assert.Equal(900, await table.AddAsync(Character, 1000, 900, CancellationToken.None));
            Assert.Equal(900, await table.AddAsync(Character, 500, 900, CancellationToken.None));
            Assert.Equal(0, await table.AddAsync(Character, -5000, 900, CancellationToken.None));
        }
        finally
        {
            Discard(path);
        }
    }

    /// <summary>
    /// A character short of the price keeps every coin, where adding would have emptied it.
    /// </summary>
    /// <remarks>
    /// <b>The whole reason there are two statements rather than one.</b> Both halves are asserted
    /// on the same character and in the same case: that a take refuses is only worth something
    /// beside the fact that the addition clamps at zero and answers a figure, which a caller
    /// cannot tell from a charge that went through. The take is first -- it has to leave the
    /// figure untouched for the addition after it to have the same amount to empty.
    /// </remarks>
    [Fact]
    public async Task A_character_short_of_the_price_keeps_every_coin()
    {
        var (table, path) = Open();
        try
        {
            await table.AddAsync(Character, 299, NoCap, CancellationToken.None);

            Assert.Null(await table.TryTakeAsync(Character, 300, CancellationToken.None));
            Assert.Equal(299, await Carried(table));
            Assert.Equal(0, await table.AddAsync(Character, -300, NoCap, CancellationToken.None));
        }
        finally
        {
            Discard(path);
        }
    }

    /// <summary>The last coin can be spent; an emptied purse is one that paid.</summary>
    /// <remarks>
    /// The boundary of the condition. A take that required the character to hold more than the
    /// price would refuse exactly the player who can just afford something, which is the one case
    /// a charge has to get right.
    /// </remarks>
    [Fact]
    public async Task Paying_out_everything_carried_is_paying_and_not_refusing()
    {
        var (table, path) = Open();
        try
        {
            await table.AddAsync(Character, 250, NoCap, CancellationToken.None);

            Assert.Equal(0, await table.TryTakeAsync(Character, 250, CancellationToken.None));
            Assert.Equal(0, await Carried(table));
        }
        finally
        {
            Discard(path);
        }
    }

    /// <summary>Taking nothing is allowed; taking a negative amount is not.</summary>
    /// <remarks>
    /// A charge of nothing is a charge every character can afford, including one carrying zero. A
    /// negative take is an addition wearing the other name, and handing gold out is the one thing
    /// a charging seam must not be able to do by accident.
    /// </remarks>
    [Fact]
    public async Task Taking_nothing_is_allowed_and_taking_a_negative_amount_is_not()
    {
        var (table, path) = Open();
        try
        {
            Assert.Equal(0, await table.TryTakeAsync(Character, 0, CancellationToken.None));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await table.TryTakeAsync(Character, -50, CancellationToken.None));
        }
        finally
        {
            Discard(path);
        }
    }

    /// <summary>A character this server does not have carries nothing and is given nothing.</summary>
    /// <remarks>
    /// Neither direction opens a row. A statement that matched nothing means the character does
    /// not exist, and inventing one here would put a figure on a character nobody can play.
    /// </remarks>
    [Fact]
    public async Task A_character_that_does_not_exist_is_neither_paid_nor_charged()
    {
        var (table, path) = Open();
        var absent = new CharacterId(9999);
        try
        {
            Assert.Equal(0, await table.AddAsync(absent, 500, NoCap, CancellationToken.None));
            Assert.Null(await table.FindAsync(absent, CancellationToken.None));
            Assert.Null(await table.TryTakeAsync(absent, 1, CancellationToken.None));
        }
        finally
        {
            Discard(path);
        }
    }

    /// <remarks>
    /// Constructing the database is what creates the schema. The character's own row is opened
    /// here because the table under test does not open one -- that belongs to character creation.
    /// </remarks>
    private static (SqliteCharacterCarriedGoldTable Table, string Path) Open()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-carried-gold-{Guid.NewGuid():N}.db");
        var database = new SqliteDatabase(path, new DomainTransactionScope());
        using (var connection = new SqliteConnection(database.ConnectionString))
        {
            connection.Open();
            using var insert = connection.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO characters(character_id, account_id, name, job, level)
                VALUES (@characterId, 1, 'carrier', 1, @level);
                INSERT INTO character_stats(character_id) VALUES (@characterId);
                """;
            insert.Parameters.AddWithValue("@characterId", Character.Value);
            insert.Parameters.AddWithValue("@level", CharacterLevel);
            insert.ExecuteNonQuery();
        }

        return (new SqliteCharacterCarriedGoldTable(database), path);
    }

    /// <summary>Lets go of the file, and does not fail a passing test if the pool still holds it.</summary>
    private static void Discard(string path)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A temporary file the operating system will clear. Failing here would report a
            // passing assertion as a broken one.
        }
    }
}
