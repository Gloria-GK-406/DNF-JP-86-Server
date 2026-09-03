using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// The seam every movement of the account cargo's gold goes through, over the store that
/// actually moves it.
/// </summary>
/// <remarks>
/// Against real SQLite rather than a stand-in table, because the whole of what is under test is
/// where the refusal comes from. Both directions are one statement in the store -- the condition
/// that says the account can pay and the subtraction that makes it pay are the same statement --
/// so a fake table would be asserting the fake's own arithmetic and would go on passing if the
/// statement lost its condition.
/// </remarks>
public sealed class AccountCargoGoldServiceTests
{
    private static readonly AccountId Account = new(31);

    /// <summary>An account carrying more than the price pays it and keeps the rest.</summary>
    [Fact]
    public async Task An_account_that_can_pay_is_charged_and_keeps_the_rest()
    {
        var (cargoGold, path) = Open();
        try
        {
            await cargoGold.AddAsync(Account, 10300, CancellationToken.None);

            Assert.Equal(300, await cargoGold.TrySpendAsync(Account, 10000, CancellationToken.None));
        }
        finally
        {
            Discard(path);
        }
    }

    /// <summary>
    /// An account short of the price is refused, and still holds every coin afterwards.
    /// </summary>
    /// <remarks>
    /// The balance is read back through the seam itself rather than trusted from the refusal,
    /// because "answered null" and "left the gold alone" are two claims and only the second one
    /// is what a player would notice. Adding nothing is how it is read: the addition answers the
    /// total, and adding zero to it cannot change what it answers.
    /// </remarks>
    [Fact]
    public async Task An_account_that_cannot_pay_is_refused_and_keeps_its_gold()
    {
        var (cargoGold, path) = Open();
        try
        {
            await cargoGold.AddAsync(Account, 10299, CancellationToken.None);

            Assert.Null(await cargoGold.TrySpendAsync(Account, 10300, CancellationToken.None));
            Assert.Equal(10299, await cargoGold.AddAsync(Account, 0, CancellationToken.None));
        }
        finally
        {
            Discard(path);
        }
    }

    /// <summary>
    /// A charge below zero is refused outright rather than handed to the store.
    /// </summary>
    /// <remarks>
    /// It would reach the store as a take of negative gold, which the store's own condition finds
    /// affordable and its subtraction turns into a gift. A charging seam that can hand gold out
    /// by getting a sign wrong is the defect this guard exists for, so it fails loudly instead.
    /// </remarks>
    [Fact]
    public async Task A_charge_below_zero_is_refused_rather_than_paid_out()
    {
        var (cargoGold, path) = Open();
        try
        {
            await cargoGold.AddAsync(Account, 100, CancellationToken.None);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await cargoGold.TrySpendAsync(Account, -50, CancellationToken.None));
            Assert.Equal(100, await cargoGold.AddAsync(Account, 0, CancellationToken.None));
        }
        finally
        {
            Discard(path);
        }
    }

    /// <remarks>Constructing the database is what creates the schema.</remarks>
    private static (AccountCargoGoldService CargoGold, string Path) Open()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-cargo-gold-{Guid.NewGuid():N}.db");
        return (
            new AccountCargoGoldService(
                new SqliteAccountCargoStateTable(
                    new SqliteDatabase(path, new DomainTransactionScope()))),
            path);
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
