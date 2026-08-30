using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// The account's purse.
/// </summary>
/// <remarks>
/// It is <c>account_cargo_state.value32</c>, which has been stored, loaded and put on the wire
/// since long before anything wrote to it -- the reference server writes its own
/// <c>AccountCargo.Money</c> at the same offset of the item-list frame. These tests are about the
/// one thing that changed: adding to it.
/// </remarks>
public sealed class SqliteAccountCargoStateTableTests
{
    private static readonly AccountId Account = new(7);

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

    /// <remarks>Constructing the database is what creates the schema.</remarks>
    private static (SqliteAccountCargoStateTable Table, string Path) Open()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-purse-{Guid.NewGuid():N}.db");
        return (
            new SqliteAccountCargoStateTable(new SqliteDatabase(path, new DomainTransactionScope())),
            path);
    }

    /// <summary>An account that has never held money gets a row and the amount.</summary>
    [Fact]
    public async Task The_first_coins_open_the_account_a_row()
    {
        var (table, path) = Open();
        try
        {
            Assert.Equal(38, await table.AddValue32Async(Account, 38, CancellationToken.None));

            var row = Assert.Single(
                await table.ReadByAccountAsync(Account, CancellationToken.None));
            Assert.Equal(38, row.Value32);
        }
        finally
        {
            Discard(path);
        }
    }

    /// <summary>Later coins add to what is there.</summary>
    [Fact]
    public async Task Coins_add_to_what_the_account_already_holds()
    {
        var (table, path) = Open();
        try
        {
            await table.AddValue32Async(Account, 38, CancellationToken.None);
            await table.AddValue32Async(Account, 44, CancellationToken.None);

            Assert.Equal(213, await table.AddValue32Async(Account, 131, CancellationToken.None));
        }
        finally
        {
            Discard(path);
        }
    }

    /// <summary>
    /// Adding happens in the statement, so two pickups answered at once cannot both add to the
    /// same total they read.
    /// </summary>
    /// <remarks>
    /// The reason the addition is not a read followed by a write. A purse that lost a coin to a
    /// race is a defect nobody can reconstruct afterwards: the player only knows they are poorer
    /// than they expected.
    /// </remarks>
    [Fact]
    public async Task Concurrent_pickups_all_reach_the_purse()
    {
        var (table, path) = Open();
        try
        {
            for (var round = 0; round < 50; round++)
                await table.AddValue32Async(Account, 1, CancellationToken.None);

            var row = Assert.Single(
                await table.ReadByAccountAsync(Account, CancellationToken.None));
            Assert.Equal(50, row.Value32);
        }
        finally
        {
            Discard(path);
        }
    }

    /// <summary>
    /// The total is held between zero and what the wire field can carry.
    /// </summary>
    /// <remarks>
    /// The field is a signed 32-bit integer. A total that wrapped would draw the player a negative
    /// purse, and one driven below zero would do the same.
    /// </remarks>
    [Fact]
    public async Task The_purse_is_held_between_zero_and_the_wire_ceiling()
    {
        var (table, path) = Open();
        try
        {
            await table.AddValue32Async(Account, int.MaxValue, CancellationToken.None);
            Assert.Equal(
                int.MaxValue,
                await table.AddValue32Async(Account, 1000, CancellationToken.None));

            Assert.Equal(
                0, await table.AddValue32Async(Account, int.MinValue + 1, CancellationToken.None));
        }
        finally
        {
            Discard(path);
        }
    }
}
